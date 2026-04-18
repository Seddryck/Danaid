# File Organization

## One class per file

Each class must be defined in its own file.

Rules:
- Do not place multiple classes in the same `.cs` file.
- The file name must exactly match the class name.
- A file containing a class must not contain another top-level class.
- Helper, nested, or secondary classes must not be introduced in the same file unless explicitly required by the language construct or already mandated by an existing framework pattern.
- If a new class is needed, create a new file for it.

Examples:
- `OrderService` must be in `OrderService.cs`
- `CustomerRepository` must be in `CustomerRepository.cs`

### Related guidance

- Interfaces must also be in their own file.
- Enums must be in their own file.
- Records must be in their own file.
- Exceptions must be in their own file.
- Avoid grouping types in a single file for convenience.

### Expected behavior when generating code

When generating or modifying code:
- always create a separate file for each new class
- never append a new class to an existing file containing another class
- preserve the one-type-per-file structure consistently across the project
