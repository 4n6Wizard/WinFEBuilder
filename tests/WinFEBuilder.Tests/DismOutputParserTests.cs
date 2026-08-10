using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class DismOutputParserTests
{
    private const string ListOutput = @"
Deployment Image Servicing and Management tool
Version: 10.0.26100.1

Details for image : C:\ws\sources\boot.wim

Index : 1
Name : Microsoft Windows PE (amd64)
Description : Microsoft Windows PE (amd64)
Size : 1,505,290,858 bytes

The operation completed successfully.
";

    private const string DetailOutput = @"
Deployment Image Servicing and Management tool
Version: 10.0.26100.1

Details for image : C:\ws\sources\boot.wim

Index : 1
Name : Microsoft Windows PE (amd64)
Description : Microsoft Windows PE (amd64)
Architecture : x64
Hal : <undefined>
Version : 10.0.26100
Edition : WindowsPE

The operation completed successfully.
";

    [Fact]
    public void ParseImageList_ReadsIndexNameDescriptionSize()
    {
        var images = DismOutputParser.ParseImageList(ListOutput);
        Assert.Single(images);
        Assert.Equal(1, images[0].Index);
        Assert.Equal("Microsoft Windows PE (amd64)", images[0].Name);
        Assert.Equal("Microsoft Windows PE (amd64)", images[0].Description);
        Assert.Equal(1_505_290_858, images[0].SizeBytes);
    }

    [Fact]
    public void ParseImageList_HandlesMultipleImages()
    {
        var two = ListOutput + "\nIndex : 2\nName : Second\nDescription : Second image\n";
        var images = DismOutputParser.ParseImageList(two);
        Assert.Equal(2, images.Count);
        Assert.Equal("Second", images[1].Name);
    }

    [Fact]
    public void ParseArchitecture_ReadsArchitecture()
        => Assert.Equal("x64", DismOutputParser.ParseArchitecture(DetailOutput));

    [Fact]
    public void IndicatesSuccess_DetectsSuccessLine()
    {
        Assert.True(DismOutputParser.IndicatesSuccess(ListOutput));
        Assert.False(DismOutputParser.IndicatesSuccess("Error: 0x80070002"));
    }
}
