using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Transformation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntegrationConnector.Tests;

public class DataTransformerTests
{
    private readonly DataTransformer _transformer = new();

    [Fact]
    public void Transform_MapsSimpleField()
    {
        var source = "{\"cliente\":{\"nome\":\"Ana\"}}";
        var mappings = new List<MappingRule>
        {
            new() { SourcePath = "$.cliente.nome", TargetPath = "customer.name" }
        };

        var result = JObject.Parse(_transformer.Transform(source, mappings));

        Assert.Equal("Ana", result["customer"]!["name"]!.Value<string>());
    }

    [Fact]
    public void Transform_AppliesToUpperFunction()
    {
        var source = "{\"nome\":\"ana\"}";
        var mappings = new List<MappingRule>
        {
            new() { SourcePath = "$.nome", TargetPath = "name", Function = TransformFunction.ToUpper }
        };

        var result = JObject.Parse(_transformer.Transform(source, mappings));

        Assert.Equal("ANA", result["name"]!.Value<string>());
    }

    [Fact]
    public void Transform_AppliesDefaultWhenSourceMissing()
    {
        var source = "{}";
        var mappings = new List<MappingRule>
        {
            new() { SourcePath = "$.inexistente", TargetPath = "status", Function = TransformFunction.Default, FunctionArgument = "pendente" }
        };

        var result = JObject.Parse(_transformer.Transform(source, mappings));

        Assert.Equal("pendente", result["status"]!.Value<string>());
    }

    [Fact]
    public void Transform_HandlesArrayOfRecords()
    {
        var source = "[{\"id\":1},{\"id\":2}]";
        var mappings = new List<MappingRule>
        {
            new() { SourcePath = "$.id", TargetPath = "identifier" }
        };

        var result = JArray.Parse(_transformer.Transform(source, mappings));

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]["identifier"]!.Value<int>());
        Assert.Equal(2, result[1]["identifier"]!.Value<int>());
    }

    [Fact]
    public void Transform_SetsConstantValue()
    {
        var source = "{}";
        var mappings = new List<MappingRule>
        {
            new() { TargetPath = "source", Function = TransformFunction.Constant, FunctionArgument = "erp-legado" }
        };

        var result = JObject.Parse(_transformer.Transform(source, mappings));

        Assert.Equal("erp-legado", result["source"]!.Value<string>());
    }

    [Fact]
    public void Transform_SupportsNestedArrayTargetPath()
    {
        var source = "{\"sku\":\"ABC\"}";
        var mappings = new List<MappingRule>
        {
            new() { SourcePath = "$.sku", TargetPath = "items[0].sku" }
        };

        var result = JObject.Parse(_transformer.Transform(source, mappings));

        Assert.Equal("ABC", result["items"]![0]!["sku"]!.Value<string>());
    }
}
