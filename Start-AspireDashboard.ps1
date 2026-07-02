# Starts the Aspire Dashboard standalone container.
# UI: http://localhost:18888 (no login)
# OTLP ingest: http://localhost:18889

docker run --rm -p 18888:18888 -p 18889:18889 `
  -e DASHBOARD__FRONTEND__AUTHMODE=Unsecured `
  -e DASHBOARD__OTLP__AUTHMODE=ApiKey `
  -e DASHBOARD__OTLP__PRIMARYAPIKEY=demo-key `
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
