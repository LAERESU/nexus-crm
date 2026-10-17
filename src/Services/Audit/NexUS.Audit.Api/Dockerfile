# Build context: root repo. Contoh: docker build -f src/Services/Audit/NexUS.Audit.Api/Dockerfile -t nexus/audit .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/Services/Audit/NexUS.Audit.Domain/NexUS.Audit.Domain.csproj", "src/Services/Audit/NexUS.Audit.Domain/"]
COPY ["src/Services/Audit/NexUS.Audit.Application/NexUS.Audit.Application.csproj", "src/Services/Audit/NexUS.Audit.Application/"]
COPY ["src/Services/Audit/NexUS.Audit.Infrastructure/NexUS.Audit.Infrastructure.csproj", "src/Services/Audit/NexUS.Audit.Infrastructure/"]
COPY ["src/Services/Audit/NexUS.Audit.Api/NexUS.Audit.Api.csproj", "src/Services/Audit/NexUS.Audit.Api/"]
RUN dotnet restore "src/Services/Audit/NexUS.Audit.Api/NexUS.Audit.Api.csproj"
COPY ["src/Services/Audit/NexUS.Audit.Domain/", "src/Services/Audit/NexUS.Audit.Domain/"]
COPY ["src/Services/Audit/NexUS.Audit.Application/", "src/Services/Audit/NexUS.Audit.Application/"]
COPY ["src/Services/Audit/NexUS.Audit.Infrastructure/", "src/Services/Audit/NexUS.Audit.Infrastructure/"]
COPY ["src/Services/Audit/NexUS.Audit.Api/", "src/Services/Audit/NexUS.Audit.Api/"]
RUN dotnet publish "src/Services/Audit/NexUS.Audit.Api/NexUS.Audit.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.Audit.Api.dll"]
