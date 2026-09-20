using System;
using AvaloniaCadEditor.Domain.Entities;
using FluentAssertions;
using FluentAssertions.Execution;

namespace AvaloniaCadEditor.Tests.Extensions;

public static class CadAssertionsExtensions
{
    public static void BeEquivalentToCadLine(this LineEntity subject, LineEntity expected, double tolerance = 1e-6, string because = "", params object[] becauseArgs)
    {
        using (new AssertionScope())
        {
            subject.Should().NotBeNull(because, becauseArgs);
            
            if (subject == null) return;

            subject.StartPoint.X.Should().BeApproximately(expected.StartPoint.X, tolerance, "StartPoint X should match");
            subject.StartPoint.Y.Should().BeApproximately(expected.StartPoint.Y, tolerance, "StartPoint Y should match");
            subject.StartPoint.Z.Should().BeApproximately(expected.StartPoint.Z, tolerance, "StartPoint Z should match");

            subject.EndPoint.X.Should().BeApproximately(expected.EndPoint.X, tolerance, "EndPoint X should match");
            subject.EndPoint.Y.Should().BeApproximately(expected.EndPoint.Y, tolerance, "EndPoint Y should match");
            subject.EndPoint.Z.Should().BeApproximately(expected.EndPoint.Z, tolerance, "EndPoint Z should match");
        }
    }
}
