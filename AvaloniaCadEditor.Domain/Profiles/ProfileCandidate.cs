using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class ProfileCandidate
{
    public ProfileClassification Classification { get; }
    public string DisplayName { get; }
    public int SpecificityRank { get; } // 1 is highest priority / simplest
    public Func<IProfile> Factory { get; }

    public ProfileCandidate(
        ProfileClassification classification,
        string displayName,
        int specificityRank,
        Func<IProfile> factory)
    {
        Classification = classification;
        DisplayName = displayName;
        SpecificityRank = specificityRank;
        Factory = factory;
    }
}
