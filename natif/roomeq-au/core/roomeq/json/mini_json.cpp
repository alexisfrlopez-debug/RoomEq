// RoomEq — analyseur JSON minimal (spec I-AUD-07) : implémentation
// (récursive avec borne de profondeur, échappements \uXXXX y compris paires
// surrogate UTF-16).
#include "roomeq/json/mini_json.h"

#include <cmath>
#include <cstdlib>
#include <limits>

namespace roomeq::json {

namespace {

constexpr int kMaxDepth = 32;         // un preset RoomEq n'excède pas 3 niveaux
constexpr size_t kMaxTokens = 200'000; // borne mémoire ~ bandes × champs
constexpr size_t kMaxStringChars = 8'192;

class Parser {
public:
    Parser(std::string_view text, std::string& error)
        : text_(text), error_(error)
    {
    }

    bool Parse(Value& out)
    {
        SkipWs();
        if (!ParseValue(out, 0))
            return false;
        SkipWs();
        if (pos_ != text_.size()) {
            return Fail("caractère inattendu après la fin du document");
        }
        return true;
    }

private:
    bool Fail(const char* message)
    {
        if (error_.empty())
            error_ = std::string(message) + " (position " + std::to_string(pos_) + ")";
        return false;
    }

    void SkipWs()
    {
        while (pos_ < text_.size()) {
            const char c = text_[pos_];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                ++pos_;
            else
                break;
        }
    }

    bool Consume(char expected)
    {
        if (pos_ < text_.size() && text_[pos_] == expected) {
            ++pos_;
            return true;
        }
        return false;
    }

    bool CheckBudget()
    {
        if (tokenCount_++ > kMaxTokens)
            return Fail("document trop grand");
        return true;
    }

    bool ParseValue(Value& out, int depth)
    {
        if (depth > kMaxDepth)
            return Fail("profondeur dépassée");
        if (!CheckBudget())
            return false;
        if (pos_ >= text_.size())
            return Fail("document tronqué");

        switch (text_[pos_]) {
        case '{': return ParseObject(out, depth);
        case '[': return ParseArray(out, depth);
        case '"': {
            std::string s;
            if (!ParseString(s))
                return false;
            out = Value::String(std::move(s));
            return true;
        }
        case 't':
            if (text_.compare(pos_, 4, "true") == 0) {
                pos_ += 4;
                out = Value::Bool(true);
                return true;
            }
            return Fail("littéral invalide");
        case 'f':
            if (text_.compare(pos_, 5, "false") == 0) {
                pos_ += 5;
                out = Value::Bool(false);
                return true;
            }
            return Fail("littéral invalide");
        case 'n':
            if (text_.compare(pos_, 4, "null") == 0) {
                pos_ += 4;
                out = Value::Null();
                return true;
            }
            return Fail("littéral invalide");
        default:
            return ParseNumber(out);
        }
    }

    bool ParseObject(Value& out, int depth)
    {
        if (!Consume('{'))
            return Fail("objet attendu");
        out = Value::Object();
        SkipWs();
        if (Consume('}'))
            return true;

        while (true) {
            SkipWs();
            std::string key;
            if (pos_ >= text_.size() || text_[pos_] != '"')
                return Fail("clé attendue");
            if (!ParseString(key))
                return false;
            SkipWs();
            if (!Consume(':'))
                return Fail("« : » attendu");
            SkipWs();

            Value value;
            if (!ParseValue(value, depth + 1))
                return false;
            out.ObjectInsert(std::move(key), std::move(value));

            SkipWs();
            if (Consume(','))
                continue;
            if (Consume('}'))
                return true;
            return Fail("« , » ou « } » attendu");
        }
    }

    bool ParseArray(Value& out, int depth)
    {
        if (!Consume('['))
            return Fail("tableau attendu");
        out = Value::Array();
        SkipWs();
        if (Consume(']'))
            return true;

        while (true) {
            SkipWs();
            Value value;
            if (!ParseValue(value, depth + 1))
                return false;
            out.ArrayAppend(std::move(value));

            SkipWs();
            if (Consume(','))
                continue;
            if (Consume(']'))
                return true;
            return Fail("« , » ou « ] » attendu");
        }
    }

    bool ParseString(std::string& out)
    {
        if (!Consume('"'))
            return Fail("chaîne attendue");
        out.clear();
        while (pos_ < text_.size()) {
            const char c = text_[pos_++];
            if (c == '"')
                return true;
            if (static_cast<unsigned char>(c) < 0x20)
                return Fail("caractère de contrôle dans une chaîne");
            if (c != '\\') {
                out.push_back(c);
                continue;
            }
            // séquence d'échappement
            if (pos_ >= text_.size())
                return Fail("échappement tronqué");
            const char e = text_[pos_++];
            switch (e) {
            case '"': out.push_back('"'); break;
            case '\\': out.push_back('\\'); break;
            case '/': out.push_back('/'); break;
            case 'b': out.push_back('\b'); break;
            case 'f': out.push_back('\f'); break;
            case 'n': out.push_back('\n'); break;
            case 'r': out.push_back('\r'); break;
            case 't': out.push_back('\t'); break;
            case 'u': {
                unsigned cp = 0;
                if (!ParseHex4(cp))
                    return false;
                // paire surrogate UTF-16 → point de code complet
                if (cp >= 0xD800 && cp <= 0xDBFF && pos_ + 1 < text_.size()
                    && text_[pos_] == '\\' && text_[pos_ + 1] == 'u') {
                    const size_t save = pos_;
                    pos_ += 2;
                    unsigned low = 0;
                    if (ParseHex4(low) && low >= 0xDC00 && low <= 0xDFFF) {
                        cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                    } else {
                        pos_ = save; // surrogate isolé : conservé tel quel
                    }
                }
                AppendUtf8(out, cp);
                break;
            }
            default:
                return Fail("échappement inconnu");
            }
            if (out.size() > kMaxStringChars)
                return Fail("chaîne trop longue");
        }
        return Fail("chaîne non fermée");
    }

    bool ParseHex4(unsigned& out)
    {
        if (pos_ + 4 > text_.size())
            return Fail("\\u tronqué");
        out = 0;
        for (int i = 0; i < 4; ++i) {
            const char c = text_[pos_++];
            out <<= 4;
            if (c >= '0' && c <= '9')
                out |= static_cast<unsigned>(c - '0');
            else if (c >= 'a' && c <= 'f')
                out |= static_cast<unsigned>(c - 'a' + 10);
            else if (c >= 'A' && c <= 'F')
                out |= static_cast<unsigned>(c - 'A' + 10);
            else
                return Fail("hexadécimal invalide");
        }
        return true;
    }

    static void AppendUtf8(std::string& out, unsigned cp)
    {
        if (cp < 0x80) {
            out.push_back(static_cast<char>(cp));
        } else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }

    bool ParseNumber(Value& out)
    {
        const size_t start = pos_;
        if (pos_ < text_.size() && (text_[pos_] == '-' || text_[pos_] == '+'))
            ++pos_;
        bool digits = false;
        while (pos_ < text_.size() && text_[pos_] >= '0' && text_[pos_] <= '9') {
            ++pos_;
            digits = true;
        }
        if (pos_ < text_.size() && text_[pos_] == '.') {
            ++pos_;
            while (pos_ < text_.size() && text_[pos_] >= '0' && text_[pos_] <= '9') {
                ++pos_;
                digits = true;
            }
        }
        if (!digits)
            return Fail("nombre attendu");
        if (pos_ < text_.size() && (text_[pos_] == 'e' || text_[pos_] == 'E')) {
            ++pos_;
            if (pos_ < text_.size() && (text_[pos_] == '-' || text_[pos_] == '+'))
                ++pos_;
            bool expDigits = false;
            while (pos_ < text_.size() && text_[pos_] >= '0' && text_[pos_] <= '9') {
                ++pos_;
                expDigits = true;
            }
            if (!expDigits)
                return Fail("exposant invalide");
        }

        const std::string numberText(text_.substr(start, pos_ - start));
        errno = 0;
        char* end = nullptr;
        const double value = std::strtod(numberText.c_str(), &end);
        if (end == nullptr || *end != '\0')
            return Fail("nombre invalide");
        if (errno == ERANGE && (value == HUGE_VAL || value == -HUGE_VAL))
            return Fail("nombre hors plage");
        out = Value::Number(value);
        return true;
    }

    std::string_view text_;
    std::string& error_;
    size_t pos_ = 0;
    size_t tokenCount_ = 0;
};

} // namespace

bool Parse(std::string_view text, Value& out, std::string& error)
{
    error.clear();
    Parser parser(text, error);
    return parser.Parse(out);
}

} // namespace roomeq::json
