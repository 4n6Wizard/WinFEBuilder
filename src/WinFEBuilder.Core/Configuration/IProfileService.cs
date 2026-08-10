namespace WinFEBuilder.Core.Configuration;

public interface IProfileService
{
    IReadOnlyList<BuildProfile> List();
    BuildProfile? Get(string name);
    void Save(BuildProfile profile);
    void Delete(string name);
    string ProfilesFilePath { get; }
}
