# Build context: root repo. Contoh: docker build -f src/Services/User/NexUS.User.Api/Dockerfile -t nexus/user .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/Services/User/NexUS.User.Domain/NexUS.User.Domain.csproj", "src/Services/User/NexUS.User.Domain/"]
COPY ["src/Services/User/NexUS.User.Application/NexUS.User.Application.csproj", "src/Services/User/NexUS.User.Application/"]
COPY ["src/Services/User/NexUS.User.Infrastructure/NexUS.User.Infrastructure.csproj", "src/Services/User/NexUS.User.Infrastructure/"]
COPY ["src/Services/User/NexUS.User.Api/NexUS.User.Api.csproj", "src/Services/User/NexUS.User.Api/"]
RUN dotnet restore "src/Services/User/NexUS.User.Api/NexUS.User.Api.csproj"
COPY ["src/Services/User/NexUS.User.Domain/", "src/Services/User/NexUS.User.Domain/"]
COPY ["src/Services/User/NexUS.User.Application/", "src/Services/User/NexUS.User.Application/"]
COPY ["src/Services/User/NexUS.User.Infrastructure/", "src/Services/User/NexUS.User.Infrastructure/"]
COPY ["src/Services/User/NexUS.User.Api/", "src/Services/User/NexUS.User.Api/"]
RUN dotnet publish "src/Services/User/NexUS.User.Api/NexUS.User.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.User.Api.dll"]
