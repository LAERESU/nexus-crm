# Build context: root repo. Contoh: docker build -f src/Services/Auth/NexUS.Auth.Api/Dockerfile -t nexus/auth .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/Services/Auth/NexUS.Auth.Domain/NexUS.Auth.Domain.csproj", "src/Services/Auth/NexUS.Auth.Domain/"]
COPY ["src/Services/Auth/NexUS.Auth.Application/NexUS.Auth.Application.csproj", "src/Services/Auth/NexUS.Auth.Application/"]
COPY ["src/Services/Auth/NexUS.Auth.Infrastructure/NexUS.Auth.Infrastructure.csproj", "src/Services/Auth/NexUS.Auth.Infrastructure/"]
COPY ["src/Services/Auth/NexUS.Auth.Api/NexUS.Auth.Api.csproj", "src/Services/Auth/NexUS.Auth.Api/"]
RUN dotnet restore "src/Services/Auth/NexUS.Auth.Api/NexUS.Auth.Api.csproj"
COPY ["src/Services/Auth/NexUS.Auth.Domain/", "src/Services/Auth/NexUS.Auth.Domain/"]
COPY ["src/Services/Auth/NexUS.Auth.Application/", "src/Services/Auth/NexUS.Auth.Application/"]
COPY ["src/Services/Auth/NexUS.Auth.Infrastructure/", "src/Services/Auth/NexUS.Auth.Infrastructure/"]
COPY ["src/Services/Auth/NexUS.Auth.Api/", "src/Services/Auth/NexUS.Auth.Api/"]
RUN dotnet publish "src/Services/Auth/NexUS.Auth.Api/NexUS.Auth.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.Auth.Api.dll"]
