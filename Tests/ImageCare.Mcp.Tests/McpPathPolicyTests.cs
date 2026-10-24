using System;

using ImageCare.Core.Domain.Configuration;

using ImageCare.Mcp.Security;

namespace ImageCare.Mcp.Tests;

[TestFixture]
public sealed class McpPathPolicyTests
{
    private string _root = null!;
    private string _outside = null!;
    private McpPathPolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _root = TestSupport.CreateTempDirectory();
        _outside = TestSupport.CreateTempDirectory();

        var configuration = new Configuration
        {
            MediaSearchRootPaths = [_root]
        };

        _policy = new McpPathPolicy(TestSupport.CreateConfigurationService(configuration));
    }

    [TearDown]
    public void TearDown()
    {
        TestSupport.DeleteTempDirectory(_root);
        TestSupport.DeleteTempDirectory(_outside);
    }

    [Test]
    public void IsAllowed_AcceptsSearchRoot()
    {
        Assert.That(_policy.IsAllowed(_root, []), Is.True);
    }

    [Test]
    public void IsAllowed_AcceptsDescendantOfSearchRoot()
    {
        var child = Path.Combine(_root, "DCIM", "100CANON");
        Directory.CreateDirectory(child);

        Assert.That(_policy.IsAllowed(Path.Combine(child, "IMG_0001.JPG"), []), Is.True);
    }

    [Test]
    public void IsAllowed_RejectsSiblingOutsideRoot()
    {
        Assert.That(_policy.IsAllowed(Path.Combine(_outside, "IMG_0001.JPG"), []), Is.False);
    }

    [Test]
    public void IsAllowed_RejectsPathThatOnlySharesNamePrefix()
    {
        // C:\temp\abc must not allow C:\temp\abcd.
        var sibling = _root + "x";
        Directory.CreateDirectory(sibling);
        try
        {
            Assert.That(_policy.IsAllowed(sibling, []), Is.False);
        }
        finally
        {
            TestSupport.DeleteTempDirectory(sibling);
        }
    }

    [Test]
    public void IsAllowed_AcceptsAdditionalSessionRoot()
    {
        var sessionFile = Path.Combine(_outside, "IMG_0042.CR3");

        Assert.That(_policy.IsAllowed(sessionFile, [_outside]), Is.True);
    }

    [Test]
    public void IsAllowed_RejectsRelativePath()
    {
        Assert.That(_policy.IsAllowed("DCIM/IMG_0001.JPG", []), Is.False);
    }

    [Test]
    public void IsAllowed_RejectsEmptyPath()
    {
        Assert.That(_policy.IsAllowed("   ", []), Is.False);
    }
}
