# Build context: root repo. Contoh: docker build -f src/Services/Project/NexUS.Project.Api/Dockerfile -t nexus/project .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/Services/Project/NexUS.Project.Domain/NexUS.Project.Domain.csproj", "src/Services/Project/NexUS.Project.Domain/"]
COPY ["src/Services/Project/NexUS.Project.Application/NexUS.Project.Application.csproj", "src/Services/Project/NexUS.Project.Application/"]
COPY ["src/Services/Project/NexUS.Project.Infrastructure/NexUS.Project.Infrastructure.csproj", "src/Services/Project/NexUS.Project.Infrastructure/"]
COPY ["src/Services/Project/NexUS.Project.Api/NexUS.Project.Api.csproj", "src/Services/Project/NexUS.Project.Api/"]
RUN dotnet restore "src/Services/Project/NexUS.Project.Api/NexUS.Project.Api.csproj"
COPY ["src/Services/Project/NexUS.Project.Domain/", "src/Services/Project/NexUS.Project.Domain/"]
COPY ["src/Services/Project/NexUS.Project.Application/", "src/Services/Project/NexUS.Project.Application/"]
COPY ["src/Services/Project/NexUS.Project.Infrastructure/", "src/Services/Project/NexUS.Project.Infrastructure/"]
COPY ["src/Services/Project/NexUS.Project.Api/", "src/Services/Project/NexUS.Project.Api/"]
RUN dotnet publish "src/Services/Project/NexUS.Project.Api/NexUS.Project.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.Project.Api.dll"]
