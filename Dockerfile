# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore only the project file first so image layers cache package restores.
COPY Kue.Api/Kue.Api.csproj Kue.Api/
RUN dotnet restore Kue.Api/Kue.Api.csproj

COPY Kue.Api/ Kue.Api/
RUN dotnet publish Kue.Api/Kue.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

# Most PaaS providers (including Render) inject PORT. Fall back to 8080 when
# running the image locally.
EXPOSE 8080
CMD ["sh", "-c", "dotnet Kue.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
