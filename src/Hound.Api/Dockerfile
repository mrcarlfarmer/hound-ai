FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Hound.Core/Hound.Core.csproj Hound.Core/
COPY src/Hound.Api/Hound.Api.csproj Hound.Api/
COPY Directory.Build.props ../
RUN dotnet restore Hound.Api/Hound.Api.csproj

COPY src/Hound.Core/ Hound.Core/
COPY src/Hound.Api/ Hound.Api/
RUN dotnet publish Hound.Api/Hound.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Hound.Api.dll"]
