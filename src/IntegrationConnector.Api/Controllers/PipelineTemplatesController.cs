using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationConnector.Api.Controllers;

/// <summary>Modelos prontos de definição de pipeline para os casos de uso mais comuns, usados como ponto de partida.</summary>
[ApiController]
[Route("api/pipeline-templates")]
public class PipelineTemplatesController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll() => Ok(new[]
    {
        new
        {
            Name = "REST → Banco de Dados",
            Description = "Lê uma lista de registros de uma API REST e grava (upsert) em uma tabela relacional.",
            Definition = new PipelineDefinition
            {
                SourceOperation = new ConnectorOperation { Action = "GET", Target = "/registros" },
                TargetOperation = new ConnectorOperation { Action = "Text", Target = "INSERT INTO tabela (id, nome) VALUES (@id, @nome) ON CONFLICT (id) DO UPDATE SET nome = @nome" },
                Mappings = new List<MappingRule>
                {
                    new() { SourcePath = "$.id", TargetPath = "id" },
                    new() { SourcePath = "$.nome", TargetPath = "nome", Function = TransformFunction.Trim }
                }
            }
        },
        new
        {
            Name = "Banco de Dados → Fila",
            Description = "Lê registros pendentes de uma tabela e publica cada um como mensagem em uma fila RabbitMQ.",
            Definition = new PipelineDefinition
            {
                SourceOperation = new ConnectorOperation { Action = "Text", Target = "SELECT * FROM eventos_pendentes" },
                TargetOperation = new ConnectorOperation { Action = "Send", Target = "fila-eventos" },
                Mappings = new List<MappingRule>
                {
                    new() { SourcePath = "$.id", TargetPath = "eventId" },
                    new() { SourcePath = "$.payload", TargetPath = "data" }
                }
            }
        },
        new
        {
            Name = "Arquivo CSV → REST",
            Description = "Lê um arquivo CSV local e envia cada linha como um POST para uma API REST.",
            Definition = new PipelineDefinition
            {
                SourceOperation = new ConnectorOperation { Action = "Read", Target = "entrada.csv", Format = PayloadFormat.Csv },
                TargetOperation = new ConnectorOperation { Action = "POST", Target = "/importar" },
                Mappings = new List<MappingRule>
                {
                    new() { SourcePath = "$.Nome", TargetPath = "nome" },
                    new() { SourcePath = "$.Email", TargetPath = "email", Function = TransformFunction.ToLower }
                }
            }
        }
    });
}
