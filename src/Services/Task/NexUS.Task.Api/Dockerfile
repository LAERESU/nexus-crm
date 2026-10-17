# Build context: root repo. Contoh: docker build -f src/Services/Task/NexUS.Task.Api/Dockerfile -t nexus/task .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/Services/Task/NexUS.Task.Domain/NexUS.Task.Domain.csproj", "src/Services/Task/NexUS.Task.Domain/"]
COPY ["src/Services/Task/NexUS.Task.Application/NexUS.Task.Application.csproj", "src/Services/Task/NexUS.Task.Application/"]
COPY ["src/Services/Task/NexUS.Task.Infrastructure/NexUS.Task.Infrastructure.csproj", "src/Services/Task/NexUS.Task.Infrastructure/"]
COPY ["src/Services/Task/NexUS.Task.Api/NexUS.Task.Api.csproj", "src/Services/Task/NexUS.Task.Api/"]
RUN dotnet restore "src/Services/Task/NexUS.Task.Api/NexUS.Task.Api.csproj"
COPY ["src/Services/Task/NexUS.Task.Domain/", "src/Services/Task/NexUS.Task.Domain/"]
COPY ["src/Services/Task/NexUS.Task.Application/", "src/Services/Task/NexUS.Task.Application/"]
COPY ["src/Services/Task/NexUS.Task.Infrastructure/", "src/Services/Task/NexUS.Task.Infrastructure/"]
COPY ["src/Services/Task/NexUS.Task.Api/", "src/Services/Task/NexUS.Task.Api/"]
RUN dotnet publish "src/Services/Task/NexUS.Task.Api/NexUS.Task.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.Task.Api.dll"]
