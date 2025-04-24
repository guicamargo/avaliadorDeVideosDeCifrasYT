var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Configura��o do builder
builder.Services.AddControllersWithViews(); // Adiciona suporte a MVC com Views
builder.Services.AddHttpClient(); // Adiciona suporte a HttpClient


var app = builder.Build();
// Configura��o do app
app.UseStaticFiles(); // Para servir arquivos est�ticos
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=YouTubeAnalysis}/{action=Index}/{id?}");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
