using Samples.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSamplesService();

builder.Build().Run();
