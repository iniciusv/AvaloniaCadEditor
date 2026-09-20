using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Application.Services;

public interface IProfileRecognitionService
{
    IReadOnlyList<IProfile> RecognizeProfiles(Sketch sketch);
}
