using Kape22Importer;

var builder = Host.CreateApplicationBuilder(args);

// Persistence, the FR-8 startup compatibility gate, and the Worker are wired together so the gate
// always runs before the Worker (Story 2.5, NFR-5, CC-7).
builder.Services.AddKape22Startup(builder.Configuration);

var host = builder.Build();
host.Run();
