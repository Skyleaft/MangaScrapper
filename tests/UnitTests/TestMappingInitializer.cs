using System.Reflection;
using MangaScrapper.Core.Common.Mappings;
using Mapster;

namespace UnitTests;

public static class TestMappingInitializer
{
    private static readonly object Lock = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        lock (Lock)
        {
            if (_initialized) return;
            try
            {
                TypeAdapterConfig.GlobalSettings.Scan(typeof(MangaMappingConfig).Assembly);
            }
            catch (InvalidOperationException)
            {
                // Already compiled by another test
            }
            _initialized = true;
        }
    }
}
