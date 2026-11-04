using Microsoft.Extensions.Logging;
using TaskPane.Core.Entities;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Services;

public sealed class OrganizationService
{
    private readonly IProjectRepository _projects;
    private readonly ITagRepository _tags;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IProjectRepository projects,
        ITagRepository tags,
        IUnitOfWork unitOfWork,
        ILogger<OrganizationService> logger)
    {
        _projects = projects;
        _tags = tags;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task<IReadOnlyList<Project>> ListProjectsAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
        => _projects.ListAsync(includeArchived, cancellationToken);

    public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
        => _tags.ListAsync(cancellationToken);

    public async Task<Project> CreateProjectAsync(string name, string? color = null, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name, ValidationCodes.ProjectNameRequired);
        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            Id = ProjectId.New(),
            Name = normalized,
            Color = color,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _projects.InsertAsync(project, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created project {ProjectId}", project.Id);
        return project;
    }

    public async Task<Tag> CreateTagAsync(string name, string? color = null, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name, ValidationCodes.TagNameRequired);
        var existing = await _tags.FindByNameAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new BusinessException(ValidationCodes.TagDuplicate, "A tag with that name already exists.");
        }

        var tag = new Tag
        {
            Id = TagId.New(),
            Name = normalized,
            Color = color,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tags.InsertAsync(tag, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created tag {TagId}", tag.Id);
        return tag;
    }

    public async Task<Tag> GetOrCreateTagAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name, ValidationCodes.TagNameRequired);
        var existing = await _tags.FindByNameAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        return await CreateTagAsync(normalized, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<TagId>> GetTaskTagIdsAsync(TaskId taskId, CancellationToken cancellationToken = default)
        => _tags.GetTagIdsForTaskAsync(taskId, cancellationToken);

    public async Task SetTaskTagsAsync(
        TaskId taskId,
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var tagId in tagIds)
        {
            var tag = await _tags.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false);
            if (tag is null)
            {
                throw new BusinessException(ValidationCodes.TagNotFound, "One or more tags were not found.");
            }
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tags.SetTaskTagsAsync(taskId, tagIds, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeName(string name, string code)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException(code, "Name is required.");
        }

        return name.Trim();
    }
}
