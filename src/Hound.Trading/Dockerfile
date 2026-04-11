FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Hound.Core/Hound.Core.csproj Hound.Core/
COPY src/Hound.Trading/Hound.Trading.csproj Hound.Trading/
COPY Directory.Build.props ../
RUN dotnet restore Hound.Trading/Hound.Trading.csproj

COPY src/Hound.Core/ Hound.Core/
COPY src/Hound.Trading/ Hound.Trading/
RUN dotnet publish Hound.Trading/Hound.Trading.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Hound.Trading.dll"]
