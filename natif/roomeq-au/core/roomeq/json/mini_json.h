// RoomEq — analyseur JSON minimal (spec I-AUD-07). Aucune dépendance, aucune
// allocation dynamique incontrôlée : profondeur bornée, tailles bornées,
// syntaxe stricte (RFC 8259, sans nombres exotiques), pour lire les presets
// .json produits par l'application RoomEq.
#pragma once

#include <map>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace roomeq::json {

/// Valeur JSON (DOM minimaliste).
class Value {
public:
    enum class Kind { Null, Bool, Number, String, Array, Object };

    Value() = default;

    static Value Null() { return Value{}; }
    static Value Bool(bool b) { Value v; v.kind_ = Kind::Bool; v.bool_ = b; return v; }
    static Value Number(double d) { Value v; v.kind_ = Kind::Number; v.number_ = d; return v; }
    static Value String(std::string s)
    {
        Value v;
        v.kind_ = Kind::String;
        v.string_ = std::make_shared<std::string>(std::move(s));
        return v;
    }
    static Value Array() { Value v; v.kind_ = Kind::Array; v.array_ = std::make_shared<ArrayT>(); return v; }
    static Value Object() { Value v; v.kind_ = Kind::Object; v.object_ = std::make_shared<ObjectT>(); return v; }

    Kind kind() const { return kind_; }
    bool IsNull() const { return kind_ == Kind::Null; }
    bool IsBool() const { return kind_ == Kind::Bool; }
    bool IsNumber() const { return kind_ == Kind::Number; }
    bool IsString() const { return kind_ == Kind::String; }
    bool IsArray() const { return kind_ == Kind::Array; }
    bool IsObject() const { return kind_ == Kind::Object; }

    bool AsBool(bool fallback = false) const { return IsBool() ? bool_ : fallback; }
    double AsNumber(double fallback = 0.0) const { return IsNumber() ? number_ : fallback; }
    const std::string& AsString() const
    {
        static const std::string empty;
        return IsString() && string_ ? *string_ : empty;
    }

    /// Tableau : taille et accès (index hors bornes → valeur nulle).
    size_t Size() const { return IsArray() && array_ ? array_->size() : 0; }
    const Value& At(size_t index) const
    {
        static const Value null;
        if (IsArray() && array_ && index < array_->size())
            return (*array_)[index];
        return null;
    }

    /// Objet : accès par clé (absente → valeur nulle).
    const Value& Get(std::string_view key) const
    {
        static const Value null;
        if (IsObject() && object_) {
            auto it = object_->find(std::string(key));
            if (it != object_->end())
                return it->second;
        }
        return null;
    }
    bool Has(std::string_view key) const
    {
        return IsObject() && object_ && object_->count(std::string(key)) != 0;
    }

    // construction (réservé à l'analyseur)
    void ArrayAppend(Value value)
    {
        if (array_)
            array_->push_back(std::move(value));
    }
    void ObjectInsert(std::string key, Value value)
    {
        if (object_)
            (*object_)[std::move(key)] = std::move(value);
    }

    using ArrayT = std::vector<Value>;
    using ObjectT = std::map<std::string, Value>;

private:
    Kind kind_ = Kind::Null;
    bool bool_ = false;
    double number_ = 0.0;
    std::shared_ptr<std::string> string_;
    std::shared_ptr<ArrayT> array_;
    std::shared_ptr<ObjectT> object_;
};

/// Analyse un document JSON complet. Retourne false et remplit `error` en cas
/// d'échec (document tronqué, séquence invalide, profondeur ou taille dépassée).
bool Parse(std::string_view text, Value& out, std::string& error);

} // namespace roomeq::json
