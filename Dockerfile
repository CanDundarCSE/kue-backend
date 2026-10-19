# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore only the project file first so package restores stay cached when
# application code changes.
COPY Kue.Api.csproj ./
RUN dotnet restore Kue.Api.csproj

COPY . ./
RUN dotnet publish Kue.Api.csproj \
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
