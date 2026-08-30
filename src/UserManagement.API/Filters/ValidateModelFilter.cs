using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UserManagement.API.Common;

namespace UserManagement.API.Filters;

/// <summary>Returns model binding/DataAnnotations failures in the standard ApiResponse envelope.</summary>
public class ValidateModelFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;

        var errors = context.ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value!.Errors.Select(e =>
                    string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());

        context.Result = new BadRequestObjectResult(
            ApiResponse.Fail("One or more validation errors occurred.", errors, context.HttpContext.TraceIdentifier));
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
