using TaskPane.Core.Branding;
using TaskPane.Core.Entities;

namespace TaskPane.Core.Tests;

public sealed class AppBrandingTests
{
    [Fact]
    public void Public_default_display_name_is_TaskPane()
    {
        Assert.Equal("TaskPane", AppBranding.DefaultAppDisplayName);
        Assert.Equal(AppBranding.DefaultAppDisplayName, new AppSettings().AppDisplayName);
    }
}
