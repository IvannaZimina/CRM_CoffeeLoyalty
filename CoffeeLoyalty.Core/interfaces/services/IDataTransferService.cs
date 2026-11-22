
using CoffeeLoyalty.Core.DTOs;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Defines JSON import, export, and validation operations.
// Access is restricted to Managers by the service implementation.
public interface IDataTransferService
{
    // Exports supported application datasets to a JSON file.
    // Returns the total number of exported records.
    Task<OperationResult<int>> ExportToJsonAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    // Validates JSON structure and data without modifying the database.
    // Returns validation warnings, or an error if the file is invalid.
    Task<OperationResult<IReadOnlyList<string>>> ValidateImportAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    // Imports valid JSON data and returns the number of imported records.
    // Conflicting records are rejected; failed imports must leave data unchanged.
    Task<OperationResult<int>> ImportFromJsonAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
