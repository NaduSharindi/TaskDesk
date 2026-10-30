using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TaskDesk.Domain;
using TaskDesk.Models;
using TaskDesk.Services;

namespace TaskDesk.Controllers;

public sealed class TasksController(TaskService tasks) : Controller
{
    private UserScope CurrentUser => UserScope.From(User);

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] TaskFilter filter, CancellationToken cancellationToken)
    {
        var user = CurrentUser;
        var result = await tasks.SearchAsync(user, filter, cancellationToken);
        return View(new TaskListViewModel { Filter = filter, Result = result, CanSeeTeam = user.IsAdmin || user.IsManager });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        View(new TaskDetailsViewModel { Details = await tasks.GetDetailsAsync(CurrentUser, id, cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var user = CurrentUser;
        var model = new TaskFormViewModel { AssignedToId = user.UserId };
        await LoadAssigneesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(TaskFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var result = await tasks.CreateAsync(CurrentUser, model.ToInput(), cancellationToken);
                TempData["Success"] = $"Task {TaskItem.FormatReference(result.TaskId)} created.";
                SetWarnings(result.Warnings);
                return RedirectToAction(nameof(Details), new { id = result.TaskId });
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        await LoadAssigneesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetForEditAsync(CurrentUser, id, cancellationToken);
        var model = TaskFormViewModel.From(task);
        await LoadAssigneesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, TaskFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var result = await tasks.UpdateAsync(CurrentUser, id, model.ToInput(), model.ConcurrencyStamp, cancellationToken);
                TempData["Success"] = "Changes saved.";
                SetWarnings(result.Warnings);
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex) when (ex is BusinessRuleException or ConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        model.Id = id;
        await LoadAssigneesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> ChangeStatus(int id, WorkStatus status, CancellationToken cancellationToken)
    {
        try
        {
            await tasks.ChangeStatusAsync(CurrentUser, id, status, cancellationToken);
            TempData["Success"] = $"Status changed to {status.ToDisplay()}.";
        }
        catch (Exception ex) when (ex is BusinessRuleException or ConcurrencyException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> AddComment(int id, [Bind(Prefix = "NewComment")] CommentInputModel comment, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = $"A comment must be between 1 and {TaskItem.CommentMaxLength} characters.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await tasks.AddCommentAsync(CurrentUser, id, comment.Text, cancellationToken);
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), controllerName: null, new { id }, fragment: "comments");
    }

    private async Task LoadAssigneesAsync(TaskFormViewModel model, CancellationToken cancellationToken)
    {
        var options = await tasks.GetAssigneeOptionsAsync(CurrentUser, cancellationToken);
        model.AssigneeOptions = options
            .Select(o => new SelectListItem(o.Name, o.Id, o.Id == model.AssignedToId))
            .ToList();
    }

    private void SetWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count > 0)
        {
            TempData["Warning"] = string.Join(" ", warnings);
        }
    }
}
