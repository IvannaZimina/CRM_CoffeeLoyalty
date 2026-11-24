# CoffeeLoyalty CRM

Desktop CRM and loyalty program application for a coffee shop, built with C#, WPF (MVVM), and SQLite.

## Business Idea

CoffeeLoyalty is designed for a small coffee shop that needs a simple customer relationship
management and loyalty system. In small establishments, loyalty programs are often missing or
are managed with paper stamp cards. This makes it difficult for the owner to know who the regular
customers are, what they buy, how often they visit, and how the loyalty program affects the business.

The application stores customers, loyalty cards, bonus balances, purchases, drinks, and system
operations in one SQLite database. It applies the loyalty rules during checkout and records
important operations in an audit log. A customer provides only the minimum required information
and receives bonus points for eligible purchases.

The project is intended for educational use and demonstrates how a small business workflow can be
split into a domain Core layer, a SQLite data layer, and a WPF presentation layer.

## Target Users and Problem Solved

The application supports two system roles:

- **Cashier** — searches for a customer by loyalty card number, registers a new customer when
  necessary, selects drinks, creates a purchase, and applies available bonuses.
- **Manager** — has access to cashier operations and administrative features such as customer
  management, audit logs, and JSON data import/export.

CoffeeLoyalty addresses the following problems:

- customer information is stored centrally instead of on paper cards;
- a customer can be found by entering the loyalty card number;
- drinks and quantities are recorded as purchase items;
- bonus points are calculated and redeemed according to application rules;
- customer and loyalty-card status can be managed;
- invalid operations are rejected without corrupting customer state;
- important successful and failed operations can be reviewed in the log;
- data can be backed up or transferred using JSON import/export.

## Main Functions and Data

The Core layer provides the following business capabilities:

- customer registration, viewing, updating, and deactivation;
- loyalty card creation, lookup, activation, and blocking;
- customer lookup by loyalty card number;
- drink menu retrieval and manager-only drink management;
- purchase creation with drink quantities and calculated totals;
- bonus accrual and redemption during checkout;
- SQLite persistence and transactional updates;
- audit logging of authentication, customer, purchase, and data-transfer operations;
- manager-only JSON export, import validation, and transactional import.

The main business data consists of:

- **Customer** — customer ID, name, optional birth date, category, active status, and bonus balance;
- **Loyalty card** — card ID, linked customer ID, and active/blocked status;
- **Drink** — drink ID, name, type, price, image link, and active status;
- **Purchase** — employee, optional loyalty card, total amount, spent bonuses, earned bonuses,
  and creation timestamp;
- **Purchase item** — drink, quantity, and unit price captured for the purchase;
- **Log entry** — object type, action type, employee, description, object ID, and timestamp.

## Core Business Rules

The application validates the following rules:

1. Bonuses can be earned and redeemed only for an existing active customer.
2. A blocked or inactive loyalty card cannot be used for bonus operations.
3. A customer's bonus balance cannot become negative.
4. The number of redeemed bonuses cannot exceed the available balance.
5. A bonus discount cannot exceed the order amount.
6. Only active drinks with valid prices can be included in a purchase.
7. Invalid operations must not change the customer's balance or other persistent state.
8. Invalid authorization, checkout, customer, and data-transfer operations are reported as
   structured failures and logged where the relevant service performs the audit operation.
9. Purchase persistence and related balance updates are performed transactionally so that a
   failure can roll the operation back.

The current educational loyalty formula is fixed at five bonus points for one euro. Loyalty
categories are stored in the database and can be assigned to customers, but category-specific
accrual rates and upgrade thresholds are intentionally not active in the current version.

## Team
- [Ivanna Zimina](https://github.com/IvannaZimina)
- [Arina Šiškova](https://github.com/arinashishkova)

## Technologies Used
- Programming Language: C#
- Platform: .NET 10 / WPF
- UI: XAML, data binding, commands, resource dictionaries
- Architecture: MVVM
- Database: SQLite
- Version Control: Git and GitHub
- Development Environment: Visual Studio 2022 or newer (tested target: Visual Studio 2026)

> *TODO: Update with additional libraries/packages (e.g., SQLite provider, JSON serialization) as they are added.*

Implemented packages and project dependencies:
- `Microsoft.Data.Sqlite` 10.0.11 — SQLite connection and transaction support.
- `Lucide.NET.Wpf` 0.1.2 — available icon library for the WPF layer.
- `System.Text.Json` — JSON serialization used by the Core data transfer service.
- xUnit, `Microsoft.NET.Test.Sdk`, and Coverlet — automated tests and code coverage support.

## Running Guide
1. Clone the repository: `https://github.com/IvannaZimina/CRM_CoffeeLoyalty.git`
2. Open the solution file (`CoffeeLoyalty.slnx`) in Visual Studio 2022 or newer.
3. Set `CoffeeLoyalty.WPF` as the Startup Project.
4. Press `F5` or click Start to run the application.

> *TODO: Add notes on automatic database initialization/seeding and default test credentials (Manager / Cashier) once ready.*

On application startup, `CoffeeLoyalty.WPF/App.xaml.cs` creates the SQLite connection factory,
runs `DatabaseInitializer`, applies the embedded SQL schemas, and executes idempotent seed data.
The initializer also verifies that the `DRINKS` table and seeded drinks are available.

The current seed data contains the following demonstration employee accounts:

| Role | Seeded account | Password requirement |
|---|---|---|
| Manager | `manager` | Password required; seeded password is `ChangeMe!Manager123` |
| Cashier | `cashier` | No password is checked by `AuthService` |

These credentials are for the educational project only and must be changed or moved to secure
configuration in a production application. The current authentication contract still receives an
employee name; the planned WPF role-selection screen should hide this implementation detail from
the user if employees are not to be selected by name.

## Project Documentation
Project tasks, architectural notes, and team guidelines are tracked using **GitHub Issues** and visualized on the **GitHub Projects Kanban Board**.

> *TODO: Add links to key documentation issues (ER Diagram, Guidelines & Recommendations for Collaborators, UI Resources).*

The Core implementation is documented below. Links to the corresponding GitHub Issues and
project-board cards can be added when the final issue numbers and documentation URLs are agreed.

## Project Architecture

### 1. Core — `CoffeeLoyalty.Core`
> *TODO: Describe domain models, SQLite database layer, repositories, business services, and validation logic.*

The Core project contains the application domain and does not depend on WPF. Its main areas are:

- `models` — `Customer`, `LoyaltyCard`, `Drink`, `Purchase`, `PurchaseItem`, `Employee`,
  `CustomerCategory`, `LogEntry`, `UserDrink`, and shared entity abstractions.
- `DTOs` — `CartItem`, `PurchaseRequest`, `PurchaseResult`, `OperationResult`, and
  `OperationResult<T>` for passing validated data and operation results between layers.
- `interfaces/repositories` and `interfaces/services` — contracts that separate data access
  from business logic and make the services testable.
- `repositories` — SQLite implementations for customers, cards, categories, drinks, employees,
  purchases, purchase items, user-drink counters, and logs.
- `services` — authentication/session management, customer and card management, drinks,
  checkout, bonus calculations, logging, and JSON data transfer.
- `validation` and `helpers` — password hashing, common validation, and bonus calculation rules.

The implemented Core business rules include active customer/card checks, non-negative bonus
balances, role-based permissions, authoritative drink prices loaded from the database, and
transactional checkout. Invalid checkout and authorization operations return structured failures
and are logged where the corresponding service operation reaches the logging layer.

### 2. ViewModel — `CoffeeLoyalty.WPF/ViewModels`
> *TODO: Describe MVVM ViewModels, commands, state management, and interaction between UI and Core.*

The ViewModel directory is reserved for the MVVM presentation layer. The Core services already
expose the operations needed by the future ViewModels, but the WPF ViewModels and commands are
not yet implemented. In particular, role selection, navigation state, customer selection,
cart state, bonus input, and import/export commands still need to be connected to the services.

### 3. View — `CoffeeLoyalty.WPF/Views`
> *TODO: Describe XAML views, role-based screens (Cashier / Manager), navigation, and localization resources.*

The intended views are:

- authentication and role selection;
- cashier checkout with customer/card lookup, drinks, cart, bonuses, and purchase completion;
- manager customer management;
- manager log viewer with optional date and action filters;
- manager JSON import/export dialogs.

The current `MainWindow.xaml` contains only an empty `Grid`, and the role-specific view files
are still placeholders. Therefore the WPF screens and navigation are documented as planned
functionality rather than completed functionality.

## Database & Data Layer
> *TODO: Add ER diagram overview, table descriptions (`Categories`, `Customers`, `Cards`, `Drinks`, `Purchases`, `Employees`, `User_Drink`, `Logs`), polymorphic audit logging, and JSON import/export details.*

The SQLite schema is stored in `CoffeeLoyalty.Core/data/schemas` and embedded in the Core
assembly. `DatabaseInitializer` applies the schemas in dependency order and then calls
`SeedDataFactory` inside a transaction.

The database contains the following main tables:

| Table | Purpose |
|---|---|
| `CATEGORIES` | Loyalty categories such as Basic, Silver, and Gold. |
| `CUSTOMERS` | Customer profile, birth date, status, category, and bonus balance. |
| `CARDS` | Loyalty cards linked to customers and their active/blocked status. |
| `DRINKS` | Menu drinks, prices, type, image link, and active status. |
| `EMPLOYEES` | Seeded manager/cashier accounts and authentication data. |
| `PURCHASES` | Purchase header, employee, optional card, totals, and bonus values. |
| `PURCHASE_ITEM` | Individual drinks and quantities belonging to a purchase. |
| `USER_DRINK` | Per-card/per-drink counters used by the application. |
| `LOGS` | Audit events, action type, object information, employee, description, and timestamp. |

Foreign keys, primary keys, active-status checks, positive-price checks, and non-negative bonus
balance constraints are defined in the SQL schemas. Customer and card deactivation is preferred
to physical deletion so that historical purchases and audit records remain consistent.

`DataTransferService` provides manager-only JSON export, import validation, and transactional
import. The payload includes a version and exported timestamp and can contain drinks, customers,
cards, purchases, and logs.

Loyalty categories are stored and can be changed, but category-specific accrual rates and upgrade
thresholds are intentionally not active in the current educational version. The current bonus
rule is a fixed calculation of five points per one euro, subject to the checkout and balance
validation rules.

## Design Patterns and Factories

### SeedDataFactory / DatabaseInitializer
> *TODO: Document automatic database creation and initial test data seeding logic.*

`DatabaseInitializer` opens or creates the configured SQLite database, applies all embedded SQL
schemas in dependency order, and runs the seed factory. `SeedDataFactory` inserts default
categories, drinks, employees, customers, cards, purchases, purchase items, user-drink rows,
and logs using duplicate-safe operations. Re-running initialization is intended to be idempotent.

> *TODO: Add other patterns/factories used in the project (e.g., Repository pattern, ViewModel/Service factories).*

The Core uses the Repository pattern, service-layer separation, DTOs, an operation-result pattern,
and a connection factory. `BaseRepository` centralizes SQLite connection and transaction access.
The WPF service composition and ViewModel factories have not yet been added.

## WPF UI Elements
> *TODO: Document key UI components, custom styles, resource-based localization (`Resources/`), and role-specific views.*

Branding and fallback drink image resources are present under `CoffeeLoyalty.WPF/assets/images`.
The Core contains generated resource strings in `CoreResources.resx` for validation and service
messages. The actual role-specific WPF controls, bindings, commands, navigation, and styles still
need to be implemented and connected to the Core services.

## Verified Use Cases
> *TODO: Document tested end-to-end scenarios (e.g., customer & card registration, drink order & bonus accrual/redemption, manager CRUD & audit logs, JSON export/import).*

The `CoffeeLoyalty.Core.Tests` project contains unit and SQLite integration tests for:

- authentication, logout, role permissions, and legacy password upgrade;
- customer and loyalty-card registration, updates, status changes, and repository operations;
- drink validation, filtering, manager permissions, and repository operations;
- cart and checkout DTO validation;
- bonus accrual, redemption, sufficient-balance checks, and final amount calculation;
- purchase, purchase-item, and user-drink persistence;
- transaction behavior and rollback-related data-transfer scenarios;
- log repository/service access, filtering, and protected log data handling;
- JSON import/export access control and validation.

These tests cover the Core layer. WPF UI and full end-to-end interaction tests are not yet
included. The complete cashier and manager scenarios should be considered pending until the
WPF views and ViewModels are implemented.

## Artificial Intelligence (AI) Usage Declaration
While developing this project, I used artificial intelligence (Gemini) as an architectural and technical support assistant. It was used for:
- Task analysis & action planning: Breaking down course assignment requirements into structured steps and defining the Core/WpfApp project architecture.
- OOP principles & code refinement: Assisting in structuring abstract classes and applying polymorphism correctly.
- Validation & state management: Discussing logic for defensive input validation to protect object states from corruption.
- Asynchronous programming: Discussing task execution, cancellation, progress reporting, and error handling.
- Documentation: Structuring and refining the README.md file and drafting professional code comments.