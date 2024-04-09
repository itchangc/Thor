using AIDotNet.Abstractions;
using AIDotNet.API.Service;
using AIDotNet.API.Service.DataAccess;
using AIDotNet.API.Service.Domain;
using AIDotNet.API.Service.Dto;
using AIDotNet.API.Service.Infrastructure;
using AIDotNet.API.Service.Options;
using AIDotNet.API.Service.Service;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.GetSection(JwtOptions.Name)
    .Get<JwtOptions>();

builder.Configuration.GetSection(OpenAIOptions.Name)
    .Get<OpenAIOptions>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonDateTimeConverter());
});

builder.Services.AddHttpClient();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddMapster();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description =
            "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer xxxxxxxxxxxxxxx\"",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });

    options.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Title = "FastWiki.ServiceApp",
            Version = "v1",
            Contact = new OpenApiContact { Name = "FastWiki.ServiceApp", }
        });
    foreach (var item in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.xml"))
        options.IncludeXmlComments(item, true);
    options.DocInclusionPredicate((docName, action) => true);
}).AddJwtBearerAuthentication();

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AuthorizeService>()
    .AddTransient<TokenService>()
    .AddTransient<ChatService>()
    .AddTransient<LoggerService>()
    .AddTransient<UserService>()
    .AddTransient<ChannelService>();

builder.Services.AddSingleton<IUserContext, DefaultUserContext>()
    .AddOpenAIService()
    .AddSparkDeskService();

builder.Services
    .AddCors(options =>
    {
        options.AddPolicy("AllowAll",
            builder => builder
                .SetIsOriginAllowed(_ => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());
    });

builder.Services.AddDbContext<TokenApiDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TokenApiDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapPost("/api/v1/authorize/token", async (AuthorizeService service, string account, string password) =>
        await service.TokenAsync(account, password))
    .WithGroupName("Token")
    .AddEndpointFilter<ResultFilter>()
    .WithDescription("Get token")
    .WithTags("Authorize")
    .WithOpenApi();

#region Token

var token = app.MapGroup("/api/v1/token")
    .WithGroupName("Token")
    .AddEndpointFilter<ResultFilter>()
    .RequireAuthorization()
    .WithTags("Token");

token.MapPost(string.Empty, async (TokenService service, TokenInput input) =>
        await service.CreateAsync(input))
    .WithDescription("创建Token")
    .WithOpenApi();

token.MapGet("{id}", async (TokenService service, long id) =>
        await service.GetAsync(id))
    .WithDescription("获取Token详情")
    .WithOpenApi();

token.MapGet("list", async (TokenService service, int page, int pageSize, string? token, string? keyword) =>
        await service.GetListAsync(page, pageSize, token, keyword))
    .WithDescription("获取Token列表")
    .WithOpenApi();

token.MapPut(string.Empty, async (TokenService service, Token input) =>
        await service.UpdateAsync(input))
    .WithDescription("更新Token");

token.MapDelete("{id}", async (TokenService service, long id) =>
        await service.RemoveAsync(id))
    .WithDescription("删除Token");

token.MapPut("Disable/{id}", async (TokenService service, long id) =>
        await service.DisableAsync(id))
    .WithDescription("禁用Token");

#endregion

#region Channel

var channel = app.MapGroup("/api/v1/channel")
    .WithGroupName("Channel")
    .WithTags("Channel")
    .AddEndpointFilter<ResultFilter>()
    .RequireAuthorization();

channel.MapPost("", async (ChannelService service, ChatChannelInput input) =>
    await service.CreateAsync(input));

channel.MapGet("list", async (ChannelService service, int page, int pageSize) =>
    await service.GetAsync(page, pageSize));

channel.MapDelete("{id}", async (ChannelService service, string id) =>
    await service.RemoveAsync(id));

channel.MapGet("{id}", async (ChannelService service, string id) =>
    await service.GetAsync(id));

channel.MapPut("{id}", async (ChannelService service, ChatChannelInput input, string id) =>
    await service.UpdateAsync(id, input));

channel.MapPut("/disable/{id}", async (ChannelService services, string id) =>
    await services.DisableAsync(id));

channel.MapPut("/test/{id}", async (ChannelService services, string id) =>
    await services.TestChannelAsync(id));

#endregion

#region Model

var model = app.MapGroup("/api/v1/model")
    .WithGroupName("Model")
    .WithTags("Model")
    .AddEndpointFilter<ResultFilter>()
    .RequireAuthorization();

model.MapGet("/types", ModelService.GetTypes)
    .WithDescription("获取模型类型")
    .WithOpenApi();

model.MapGet("/models",
        ModelService.GetModels)
    .WithDescription("获取模型")
    .WithOpenApi();

#endregion

#region Logger

var logger = app.MapGroup("/api/v1/logger")
    .WithGroupName("Logger")
    .WithTags("Logger")
    .AddEndpointFilter<ResultFilter>()
    .RequireAuthorization();

logger.MapGet(string.Empty,
        async (LoggerService service, int page, int pageSize, ChatLoggerType? type, string? model, DateTime? startTime,
                DateTime? endTime, string? keyword) =>
            await service.GetAsync(page, pageSize, type, model, startTime, endTime, keyword))
    .WithDescription("获取日志")
    .WithDisplayName("获取日志")
    .WithOpenApi();

#endregion

#region User

var user = app.MapGroup("/api/v1/user")
    .WithGroupName("User")
    .WithTags("User")
    .AddEndpointFilter<ResultFilter>()
    .RequireAuthorization();

user.MapPost(string.Empty, async (UserService service, CreateUserInput input) =>
    await service.CreateAsync(input));

user.MapGet(string.Empty, async (UserService service, int page, int pageSize, string? keyword) =>
    await service.GetAsync(page, pageSize, keyword));


user.MapDelete("{id}", async (UserService service, string id) =>
    await service.RemoveAsync(id));

#endregion

app.MapPost("/v1/chat/completions", async (ChatService service, HttpContext httpContext) =>
        await service.CompletionsAsync(httpContext))
    .WithGroupName("OpenAI")
    .WithDescription("Get completions from OpenAI")
    .WithOpenApi();

await app.RunAsync();