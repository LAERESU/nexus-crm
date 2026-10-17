# Build context: root repo. Contoh: docker build -f src/Services/Notification/NexUS.Notification.Api/Dockerfile -t nexus/notification .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/Services/Notification/NexUS.Notification.Domain/NexUS.Notification.Domain.csproj", "src/Services/Notification/NexUS.Notification.Domain/"]
COPY ["src/Services/Notification/NexUS.Notification.Application/NexUS.Notification.Application.csproj", "src/Services/Notification/NexUS.Notification.Application/"]
COPY ["src/Services/Notification/NexUS.Notification.Infrastructure/NexUS.Notification.Infrastructure.csproj", "src/Services/Notification/NexUS.Notification.Infrastructure/"]
COPY ["src/Services/Notification/NexUS.Notification.Api/NexUS.Notification.Api.csproj", "src/Services/Notification/NexUS.Notification.Api/"]
RUN dotnet restore "src/Services/Notification/NexUS.Notification.Api/NexUS.Notification.Api.csproj"
COPY ["src/Services/Notification/NexUS.Notification.Domain/", "src/Services/Notification/NexUS.Notification.Domain/"]
COPY ["src/Services/Notification/NexUS.Notification.Application/", "src/Services/Notification/NexUS.Notification.Application/"]
COPY ["src/Services/Notification/NexUS.Notification.Infrastructure/", "src/Services/Notification/NexUS.Notification.Infrastructure/"]
COPY ["src/Services/Notification/NexUS.Notification.Api/", "src/Services/Notification/NexUS.Notification.Api/"]
RUN dotnet publish "src/Services/Notification/NexUS.Notification.Api/NexUS.Notification.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.Notification.Api.dll"]
