// RoomEq — chaîne d'égalisation temps réel (spec I-AUD-07) : implémentation.
#include "roomeq/dsp/eq_chain.h"

#include <cmath>

namespace roomeq::dsp {

void EqChain::Configure(double sampleRate, int channels)
{
    const int ch = channels < 1 ? 1 : channels;
    const bool rebuild = ch != channels_ || std::fabs(sampleRate - sampleRate_) > 0.5
                         || filters_.empty();
    sampleRate_ = sampleRate;
    channels_ = ch;
    if (rebuild)
        Rebuild();
}

void EqChain::SetPreset(const preset::PresetDef& preset)
{
    preampDb_ = preset.preampDb;
    preampGain_ = std::pow(10.0, preset.preampDb / 20.0);
    bands_.clear();
    bands_.reserve(preset.bands.size());
    for (const auto& b : preset.bands) {
        if (b.enabled)
            bands_.push_back(BandSpec{ b.type, b.frequency, b.gainDb, b.q });
    }
    Rebuild();
}

void EqChain::SetPreampDb(double preampDb)
{
    preampDb_ = preampDb;
    preampGain_ = std::pow(10.0, preampDb / 20.0);
}

void EqChain::Reset()
{
    for (auto& column : filters_)
        for (auto& f : column)
            f.Reset();
}

void EqChain::Rebuild()
{
    preampGain_ = std::pow(10.0, preampDb_ / 20.0);
    filters_.assign(static_cast<size_t>(channels_), {});
    for (auto& column : filters_) {
        column.reserve(bands_.size());
        for (const auto& b : bands_)
            column.emplace_back(b.type, sampleRate_, b.frequency, b.gainDb, b.q);
    }
}

void EqChain::Process(float* buffer, size_t sampleCount)
{
    if (bypass_ || buffer == nullptr)
        return;
    const int ch = channels_;
    if (ch <= 0 || filters_.empty())
        return;

    const size_t frames = sampleCount / static_cast<size_t>(ch);
    const double preamp = preampGain_;
    const bool hasPreamp = std::fabs(preampDb_) > 1e-9;

    for (size_t f = 0; f < frames; ++f) {
        const size_t i0 = f * static_cast<size_t>(ch);
        for (int c = 0; c < ch; ++c) {
            auto& column = filters_[static_cast<size_t>(c)];
            double x = hasPreamp ? static_cast<double>(buffer[i0 + c]) * preamp
                                 : static_cast<double>(buffer[i0 + c]);
            for (auto& filter : column)
                x = filter.Process(x);
            buffer[i0 + c] = static_cast<float>(x);
        }
    }
}

double EqChain::ResponseDb(double freq) const
{
    double db = preampDb_;
    BiquadFilter scratch; // filtre de calcul, comme EqChain.ResponseDb (C#)
    for (const auto& b : bands_) {
        scratch.Configure(b.type, sampleRate_, b.frequency, b.gainDb, b.q);
        db += scratch.ResponseDb(freq);
    }
    return db;
}

} // namespace roomeq::dsp
