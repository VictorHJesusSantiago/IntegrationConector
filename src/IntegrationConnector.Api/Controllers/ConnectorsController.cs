using IntegrationConnector.Api.Dtos;
using IntegrationConnector.Api.Validation;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationConnector.Api.Controllers;

[ApiController]
[Route("api/connectors")]
public class ConnectorsController : ControllerBase
{
    private readonly IConnectorRepository _repository;
    private readonly IConnectorPluginFactory _pluginFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditLogRepository _auditLog;
    private readonly IConnectorHealthRepository _healthRepository;

    public ConnectorsController(
        IConnectorRepository repository,
        IConnectorPluginFactory pluginFactory,
        ISecretProtector secretProtector,
        IAuditLogRepository auditLog,
        IConnectorHealthRepository healthRepository)
    {
        _repository = repository;
        _pluginFactory = pluginFactory;
        _secretProtector = secretProtector;
        _auditLog = auditLog;
        _healthRepository = healthRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<Connector>>> GetAll(CancellationToken ct)
        => Ok(await _repository.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Connector>> GetById(Guid id, CancellationToken ct)
    {
        var connector = await _repository.GetByIdAsync(id, ct);
        return connector is null ? NotFound() : Ok(connector);
    }

    [HttpPost]
    public async Task<ActionResult<Connector>> Create(CreateConnectorRequest request, CancellationToken ct)
    {
        if (!ConnectorConfigValidator.TryValidate(request.Type, request.ConfigurationJson, out var errors))
            return ValidationProblem(BuildModelState(errors));

        var connector = new Connector
        {
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            ConfigurationJson = _secretProtector.Protect(request.ConfigurationJson)
        };

        await _repository.AddAsync(connector, ct);
        await _repository.SaveChangesAsync(ct);
        await AuditAsync("Create", "Connector", connector.Id, connector.Name, ct);
        return CreatedAtAction(nameof(GetById), new { id = connector.Id }, connector);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Connector>> Update(Guid id, UpdateConnectorRequest request, CancellationToken ct)
    {
        var connector = await _repository.GetByIdAsync(id, ct);
        if (connector is null) return NotFound();

        if (!ConnectorConfigValidator.TryValidate(connector.Type, request.ConfigurationJson, out var errors))
            return ValidationProblem(BuildModelState(errors));

        connector.Name = request.Name;
        connector.Description = request.Description;
        connector.ConfigurationJson = _secretProtector.Protect(request.ConfigurationJson);
        connector.IsActive = request.IsActive;

        _repository.Update(connector);
        await _repository.SaveChangesAsync(ct);
        await AuditAsync("Update", "Connector", connector.Id, connector.Name, ct);
        return Ok(connector);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var connector = await _repository.GetByIdAsync(id, ct);
        if (connector is null) return NotFound();

        _repository.Remove(connector);
        await _repository.SaveChangesAsync(ct);
        await AuditAsync("Delete", "Connector", connector.Id, connector.Name, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<ConnectorTestResult>> TestConnection(Guid id, CancellationToken ct)
    {
        var connector = await _repository.GetByIdAsync(id, ct);
        if (connector is null) return NotFound();

        connector.ConfigurationJson = _secretProtector.Unprotect(connector.ConfigurationJson);
        var plugin = _pluginFactory.Resolve(connector.Type);
        var result = await plugin.TestConnectionAsync(connector, ct);
        return Ok(result);
    }

    /// <summary>Última checagem de saúde registrada para cada conector (job recorrente de conectividade).</summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
        => Ok(await _healthRepository.GetLatestForAllAsync(ct));

    private static Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary BuildModelState(List<string> errors)
    {
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        foreach (var error in errors) modelState.AddModelError("ConfigurationJson", error);
        return modelState;
    }

    private async Task AuditAsync(string action, string entityType, Guid entityId, string details, CancellationToken ct)
    {
        await _auditLog.AddAsync(new Core.Entities.AuditLogEntry
        {
            Username = User.Identity?.Name ?? "anonymous",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        }, ct);
        await _auditLog.SaveChangesAsync(ct);
    }
}
