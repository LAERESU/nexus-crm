using FluentAssertions;
using NetArchTest.Rules;

namespace NexUS.Project.UnitTests;

public class ArchitectureTests
{
    private const string Root = "NexUS.Project";
    private static readonly System.Reflection.Assembly Domain = NexUS.Project.Domain.AssemblyReference.Assembly;
    private static readonly System.Reflection.Assembly Application = NexUS.Project.Application.AssemblyReference.Assembly;
    private static readonly System.Reflection.Assembly Infrastructure = NexUS.Project.Infrastructure.AssemblyReference.Assembly;

    [Fact]
    public void Domain_tidak_boleh_bergantung_pada_layer_lain()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny($"{Root}.Application", $"{Root}.Infrastructure", $"{Root}.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failing(result));
    }

    [Fact]
    public void Application_tidak_boleh_bergantung_pada_Infrastructure_atau_Api()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny($"{Root}.Infrastructure", $"{Root}.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failing(result));
    }

    [Fact]
    public void Infrastructure_tidak_boleh_bergantung_pada_Api()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOn($"{Root}.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failing(result));
    }

    private static string Failing(TestResult result) =>
        "Pelanggaran: " + string.Join(", ", result.FailingTypeNames ?? []);
}
