using System;
using System.Linq;

namespace HairNet.SourceGen.ClassEnumerations.Sample;

[ClassEnumerations]
public interface IPrerequisite
{
     bool IsMet();
}

public class OsPrerequisite : IPrerequisite
{
    public bool IsMet()
    {
        return Environment.OSVersion.Platform == PlatformID.Unix;
    }
}

public class BitnessPrerequisite : IPrerequisite
{
    public bool IsMet()
    {
        return Environment.Is64BitOperatingSystem;
    }
}

public class XdgRuntimePrerequisite : IPrerequisite
{
    public bool IsMet()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
    }
}

public class Examples
{
    public static void Main()
    {
        IPrerequisite[] prerequisites =
        {
            new BitnessPrerequisite(),
            new XdgRuntimePrerequisite(),
            new OsPrerequisite()
        };
        var result =
            PrerequisiteEnumeration.FromPrerequisites(prerequisites.Where(x => x.IsMet()).ToArray());

        if (result.Inverse().Equals(PrerequisiteEnumeration.Empty))
        {
            Console.Write("System meets all prerequisites.");
        }
        else
        {
            Console.Write("The following prerequisites were not met: {0}", result);
        }
    }
}