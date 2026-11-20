# CoffeeLoyalty CRM

Desktop CRM and loyalty program application for a coffee shop, built with C#, WPF (MVVM), and SQLite.

## Team
- [Ivanna Zimina](https://github.com/IvannaZimina)
- [Arina Šiškova](https://github.com/arinashishkova)

## Technologies Used
- Programming Language: C#
- Platform: .NET / WPF
- UI: XAML, data binding, commands, resource dictionaries
- Architecture: MVVM
- Database: SQLite
- Version Control: Git and GitHub
- Development Environment: Visual Studio 2022 or newer

> *TODO: Update with additional libraries/packages (e.g., SQLite provider, JSON serialization) as they are added.*

## Running Guide
1. Clone the repository: `https://github.com/IvannaZimina/CRM_CoffeeLoyalty.git`
2. Open the solution file (`CoffeeLoyalty.slnx`) in Visual Studio 2022 or newer.
3. Set `CoffeeLoyalty.WPF` as the Startup Project.
4. Press `F5` or click Start to run the application.

> *TODO: Add notes on automatic database initialization/seeding and default test credentials (Manager / Cashier) once ready.*

## Project Documentation
Project tasks, architectural notes, and team guidelines are tracked using **GitHub Issues** and visualized on the **GitHub Projects Kanban Board**.

> *TODO: Add links to key documentation issues (ER Diagram, Guidelines & Recommendations for Collaborators, UI Resources).*

## Project Architecture

### 1. Core — `CoffeeLoyalty.Core`
> *TODO: Describe domain models, SQLite database layer, repositories, business services, and validation logic.*

### 2. ViewModel — `CoffeeLoyalty.WPF/ViewModels`
> *TODO: Describe MVVM ViewModels, commands, state management, and interaction between UI and Core.*

### 3. View — `CoffeeLoyalty.WPF/Views`
> *TODO: Describe XAML views, role-based screens (Cashier / Manager), navigation, and localization resources.*

## Database & Data Layer
> *TODO: Add ER diagram overview, table descriptions (`Categories`, `Customers`, `Cards`, `Drinks`, `Purchases`, `Employees`, `User_Drink`, `Logs`), polymorphic audit logging, and JSON import/export details.*

## Design Patterns and Factories

### SeedDataFactory / DatabaseInitializer
> *TODO: Document automatic database creation and initial test data seeding logic.*

> *TODO: Add other patterns/factories used in the project (e.g., Repository pattern, ViewModel/Service factories).*

## WPF UI Elements
> *TODO: Document key UI components, custom styles, resource-based localization (`Resources/`), and role-specific views.*

## Verified Use Cases
> *TODO: Document tested end-to-end scenarios (e.g., customer & card registration, drink order & bonus accrual/redemption, manager CRUD & audit logs, JSON export/import).*

## Artificial Intelligence (AI) Usage Declaration
While developing this project, I used artificial intelligence (Gemini) as an architectural and technical support assistant. It was used for:
- Task analysis & action planning: Breaking down course assignment requirements into structured steps and defining the Core/WpfApp project architecture.
- OOP principles & code refinement: Assisting in structuring abstract classes and applying polymorphism correctly.
- Validation & state management: Discussing logic for defensive input validation to protect object states from corruption.
- Asynchronous programming: Discussing task execution, cancellation, progress reporting, and error handling.
- Documentation: Structuring and refining the README.md file and drafting professional code comments.