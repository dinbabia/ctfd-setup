using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var databasePath = Environment.GetEnvironmentVariable("DOTNET_DB_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "dotnet.db");
var databaseDirectory = Path.GetDirectoryName(databasePath);
if (!string.IsNullOrEmpty(databaseDirectory))
    Directory.CreateDirectory(databaseDirectory);

var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
var adminUsername = Environment.GetEnvironmentVariable("DOTNET_ADMIN_USERNAME") ?? "report_admin";
var adminPassword = Environment.GetEnvironmentVariable("DOTNET_ADMIN_PASSWORD") ?? "dotnet-admin-password";
var adminFlag = Environment.GetEnvironmentVariable("FLAG5") ?? "CTF{admin_profile_flag}";
InitializeDatabase(connectionString, adminUsername, adminPassword, adminFlag);

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", ".NET CTF API v1");
    options.DocumentTitle = ".NET CTF API - Swagger UI";
});

var tokens = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
var failedLogins = new ConcurrentDictionary<string, LoginAttempts>(StringComparer.Ordinal);
var registrations = new ConcurrentDictionary<string, RegistrationAttempts>(StringComparer.Ordinal);
var flag = Environment.GetEnvironmentVariable("FLAG4") ?? "CTF{mass_assignment_report_access}";

app.MapGet("/", () => Results.Content(
    "<!doctype html><html><body><h1>.NET CTF API</h1><p>See the <a href=\"/swagger\">API documentation</a>.</p></body></html>",
    "text/html"));

app.MapGet("/swagger/v1/swagger.json", () => Results.Json(new
{
    openapi = "3.0.3",
    info = new { title = ".NET CTF API", version = "v1" },
    paths = new Dictionary<string, object>
    {
        ["/api/register"] = new
        {
            post = new
            {
                summary = "Register an account",
                requestBody = new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/json"] = new
                        {
                            schema = new
                            {
                                type = "object",
                                required = new[] { "username", "password", "email" },
                                properties = new
                                {
                                    username = new { type = "string" },
                                    password = new { type = "string", format = "password" },
                                    email = new { type = "string", format = "email" },
                                    birthday = new { type = "string", nullable = true },
                                    address = new { type = "string", nullable = true },
                                    occupation = new { type = "string", nullable = true },
                                    gender = new { type = "string", nullable = true }
                                }
                            },
                            example = new
                            {
                                username = "alice",
                                password = "correct-horse",
                                email = "alice@example.com",
                                birthday = "2000-01-01",
                                address = "123 Example Street",
                                occupation = "Analyst",
                                gender = "female"
                            }
                        }
                    }
                },
                responses = new { @default = new { description = "Account created or validation error" } }
            }
        },
        ["/api/auth"] = new
        {
            post = new
            {
                summary = "Authenticate",
                requestBody = new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/json"] = new
                        {
                            schema = new
                            {
                                type = "object",
                                required = new[] { "username", "password" },
                                properties = new
                                {
                                    username = new { type = "string" },
                                    password = new { type = "string", format = "password" }
                                }
                            },
                            example = new
                            {
                                username = "alice",
                                password = "correct-horse"
                            }
                        }
                    }
                },
                responses = new { @default = new { description = "Bearer token or authentication error" } }
            }
        },
        ["/api/profile"] = new
        {
            get = new
            {
                summary = "Get the authenticated user's profile",
                security = new[] { new { bearerAuth = Array.Empty<string>() } },
                responses = new
                {
                    @default = new
                    {
                        description = "Profile details and account access settings; the admin account also receives FLAG5."
                    }
                }
            }
        },
        ["/api/reports"] = new
        {
            get = new
            {
                summary = "Read restricted reports",
                security = new[] { new { bearerAuth = Array.Empty<string>() } },
                responses = new { @default = new { description = "Report data or access denied" } }
            }
        },
        ["/api/reports/{id}"] = new
        {
            get = new
            {
                summary = "Get a report by ID",
                parameters = new[]
                {
                    new
                    {
                        name = "id",
                        @in = "path",
                        required = true,
                        schema = new { type = "string" },
                        example = "1"
                    }
                },
                security = new[] { new { bearerAuth = Array.Empty<string>() } },
                responses = new { @default = new { description = "Report details or access denied" } }
            }
        }
    },
    components = new
    {
        securitySchemes = new
        {
            bearerAuth = new { type = "http", scheme = "bearer" }
        }
    }
}));

app.MapPost("/api/register", (RegisterRequest request, HttpRequest httpRequest) =>
{
    var clientKey = GetClientKey(httpRequest);
    var now = DateTimeOffset.UtcNow;

    lock (registrations)
    {
        if (!registrations.TryGetValue(clientKey, out var registrationAttempts))
        {
            registrationAttempts = new RegistrationAttempts(now);
            registrations[clientKey] = registrationAttempts;
        }
        else if (registrationAttempts.BlockedUntil is { } blockedUntil)
        {
            if (blockedUntil > now)
                return Results.Json(new { error = "Registration limit reached. Try again later." }, statusCode: 429);

            registrationAttempts = new RegistrationAttempts(now);
            registrations[clientKey] = registrationAttempts;
        }
        else if (now - registrationAttempts.WindowStarted >= TimeSpan.FromMinutes(5))
        {
            registrationAttempts = new RegistrationAttempts(now);
            registrations[clientKey] = registrationAttempts;
        }

        if (registrationAttempts.SuccessfulRegistrations >= 3)
        {
            registrationAttempts.BlockedUntil = now.AddMinutes(15);
            return Results.Json(new { error = "Registration limit reached. Try again later." }, statusCode: 429);
        }

        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { error = "username, password, and email are required." });
        }

        if (!request.Email.Contains('@', StringComparison.Ordinal))
            return Results.BadRequest(new { error = "email must be a valid email address." });

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (
                username, password, email, birthday, address, occupation, gender,
                can_access_reports, can_access_admin_panel, is_admin, flag5
            ) VALUES (
                $username, $password, $email, $birthday, $address, $occupation, $gender,
                $can_access_reports, 0, 0, NULL
            )
            """;
        command.Parameters.AddWithValue("$username", request.Username.Trim());
        command.Parameters.AddWithValue("$password", EncodePassword(request.Password));
        command.Parameters.AddWithValue("$email", request.Email.Trim());
        command.Parameters.AddWithValue("$birthday", (object?)request.Birthday ?? DBNull.Value);
        command.Parameters.AddWithValue("$address", (object?)request.Address ?? DBNull.Value);
        command.Parameters.AddWithValue("$occupation", (object?)request.Occupation ?? DBNull.Value);
        command.Parameters.AddWithValue("$gender", (object?)request.Gender ?? DBNull.Value);
        command.Parameters.AddWithValue("$can_access_reports", request.CanAccessReports ? 1 : 0);

        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return Results.Conflict(new { error = "username is already registered." });
        }

        registrationAttempts.SuccessfulRegistrations++;
        return Results.Created("/api/profile", new { message = "Account created successfully." });
    }
});

app.MapPost("/api/auth", (LoginRequest request, HttpRequest httpRequest) =>
{
    var clientKey = GetClientKey(httpRequest);
    var now = DateTimeOffset.UtcNow;

    lock (failedLogins)
    {
        if (failedLogins.TryGetValue(clientKey, out var attempts))
        {
            if (attempts.BlockedUntil is { } blockedUntil)
            {
                if (blockedUntil > now)
                    return Results.Json(new { error = "Too many failed attempts. Try again later." }, statusCode: 429);
                failedLogins.TryRemove(clientKey, out _);
            }
        }

        var user = GetUser(connectionString, request.Username ?? string.Empty);
        if (user is null ||
            !string.Equals(user.Password, EncodePassword(request.Password ?? string.Empty), StringComparison.Ordinal))
        {
            var failureAttempts = failedLogins.GetOrAdd(clientKey, _ => new LoginAttempts());
            failureAttempts.FailedCount++;
            if (failureAttempts.FailedCount >= 5)
                failureAttempts.BlockedUntil = now.AddMinutes(15);
            return Results.Unauthorized();
        }

        failedLogins.TryRemove(clientKey, out _);
        var token = Guid.NewGuid().ToString("N");
        tokens[token] = user.Username;
        return Results.Ok(new { token, token_type = "Bearer" });
    }
});

app.MapGet("/api/profile", (HttpRequest httpRequest) =>
{
    if (!TryGetUser(httpRequest, tokens, connectionString, out var user))
        return Results.Unauthorized();

    var profile = new Dictionary<string, object?>
    {
        ["username"] = user.Username,
        ["email"] = user.Email,
        ["birthday"] = user.Birthday,
        ["address"] = user.Address,
        ["occupation"] = user.Occupation,
        ["gender"] = user.Gender,
        ["can_access_reports"] = user.CanAccessReports,
        ["can_access_admin_panel"] = user.CanAccessAdminPanel
    };
    if (user.IsAdmin)
        profile["flag5"] = user.Flag5;

    return Results.Ok(profile);
});

app.MapGet("/api/reports", (HttpRequest httpRequest) =>
{
    if (!TryGetUser(httpRequest, tokens, connectionString, out var user))
        return Results.Unauthorized();

    if (!user.CanAccessReports)
        return Results.Json(new { error = "Report access is required." }, statusCode: 403);

    return Results.Ok(new { report = "Quarterly operations report", flag });
});

app.MapGet("/api/reports/{id}", (string id, HttpRequest httpRequest) =>
{
    if (!TryGetUser(httpRequest, tokens, connectionString, out var user))
        return Results.Unauthorized();

    if (!user.CanAccessReports)
        return Results.Json(new { error = "Report access is required." }, statusCode: 403);

    var reportId = id.Replace('+', ' ');
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT id, title, content FROM reports WHERE id = '{reportId}'";

    using var reader = command.ExecuteReader();
    var results = new List<Dictionary<string, object?>>();
    while (reader.Read())
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var column = 0; column < reader.FieldCount; column++)
            row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
        results.Add(row);
    }

    return Results.Ok(results);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static string GetClientKey(HttpRequest request)
{
    var forwardedFor = request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedFor))
        return forwardedFor.Split(',')[0].Trim();

    return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static bool TryGetUser(
    HttpRequest request,
    ConcurrentDictionary<string, string> tokens,
    string connectionString,
    out UserAccount user)
{
    user = null!;
    var authorization = request.Headers.Authorization.FirstOrDefault();
    if (authorization is null ||
        !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return false;

    var token = authorization["Bearer ".Length..].Trim();
    if (!tokens.TryGetValue(token, out var username))
        return false;

    user = GetUser(connectionString, username)!;
    return user is not null;
}

static void InitializeDatabase(
    string connectionString,
    string adminUsername,
    string adminPassword,
    string adminFlag)
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL COLLATE NOCASE UNIQUE,
                password TEXT NOT NULL,
                email TEXT NOT NULL,
                birthday TEXT,
                address TEXT,
                occupation TEXT,
                gender TEXT,
                can_access_reports INTEGER NOT NULL DEFAULT 0,
                can_access_admin_panel INTEGER NOT NULL DEFAULT 0,
                is_admin INTEGER NOT NULL DEFAULT 0,
                flag5 TEXT
            );
            CREATE TABLE IF NOT EXISTS reports (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL,
                content TEXT NOT NULL
            );
            INSERT OR IGNORE INTO reports (id, title, content)
                VALUES (1, 'Quarterly operations report', 'Quarterly operations are within expected ranges.');
            INSERT OR IGNORE INTO reports (id, title, content)
                VALUES (2, 'Staff directory report', 'The staff directory is available to report readers.');
            """;
        command.ExecuteNonQuery();
    }

    using var seedAdmin = connection.CreateCommand();
    seedAdmin.CommandText = """
        INSERT INTO users (
            username, password, email, can_access_reports, can_access_admin_panel, is_admin, flag5
        ) VALUES (
            $username, $password, 'admin@dotnet.local', 1, 0, 1, $flag5
        )
        ON CONFLICT(username) DO UPDATE SET
            password = excluded.password,
            can_access_reports = 1,
            can_access_admin_panel = 0,
            is_admin = 1,
            flag5 = excluded.flag5
        WHERE users.is_admin = 1
        """;
    seedAdmin.Parameters.AddWithValue("$username", adminUsername);
    seedAdmin.Parameters.AddWithValue("$password", EncodePassword(adminPassword));
    seedAdmin.Parameters.AddWithValue("$flag5", adminFlag);
    seedAdmin.ExecuteNonQuery();

    using var verifyAdmin = connection.CreateCommand();
    verifyAdmin.CommandText = "SELECT is_admin FROM users WHERE username = $username";
    verifyAdmin.Parameters.AddWithValue("$username", adminUsername);
    if (Convert.ToInt32(verifyAdmin.ExecuteScalar()) != 1)
        throw new InvalidOperationException("The configured .NET admin username is already in use.");
}

static UserAccount? GetUser(string connectionString, string username)
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT username, password, email, birthday, address, occupation, gender,
               can_access_reports, can_access_admin_panel, is_admin, flag5
        FROM users
        WHERE username = $username
        """;
    command.Parameters.AddWithValue("$username", username);

    using var reader = command.ExecuteReader();
    if (!reader.Read())
        return null;

    return new UserAccount
    {
        Username = reader.GetString(0),
        Password = reader.GetString(1),
        Email = reader.GetString(2),
        Birthday = reader.IsDBNull(3) ? null : reader.GetString(3),
        Address = reader.IsDBNull(4) ? null : reader.GetString(4),
        Occupation = reader.IsDBNull(5) ? null : reader.GetString(5),
        Gender = reader.IsDBNull(6) ? null : reader.GetString(6),
        CanAccessReports = reader.GetInt32(7) != 0,
        CanAccessAdminPanel = reader.GetInt32(8) != 0,
        IsAdmin = reader.GetInt32(9) != 0,
        Flag5 = reader.IsDBNull(10) ? null : reader.GetString(10)
    };
}

static string EncodePassword(string password) =>
    Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

sealed class RegisterRequest
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Email { get; init; }
    public string? Birthday { get; init; }
    public string? Address { get; init; }
    public string? Occupation { get; init; }
    public string? Gender { get; init; }
    [JsonPropertyName("can_access_reports")]
    public bool CanAccessReports { get; init; }
    [JsonPropertyName("can_access_admin_panel")]
    public bool CanAccessAdminPanel { get; init; }
}

sealed class LoginRequest
{
    public string? Username { get; init; }
    public string? Password { get; init; }
}

sealed class UserAccount
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string Email { get; init; }
    public string? Birthday { get; init; }
    public string? Address { get; init; }
    public string? Occupation { get; init; }
    public string? Gender { get; init; }
    public bool CanAccessReports { get; init; }
    public bool CanAccessAdminPanel { get; init; }
    public bool IsAdmin { get; init; }
    public string? Flag5 { get; init; }
}

sealed class LoginAttempts
{
    public int FailedCount { get; set; }
    public DateTimeOffset? BlockedUntil { get; set; }
}

sealed class RegistrationAttempts(DateTimeOffset windowStarted)
{
    public DateTimeOffset WindowStarted { get; } = windowStarted;
    public int SuccessfulRegistrations { get; set; }
    public DateTimeOffset? BlockedUntil { get; set; }
}
