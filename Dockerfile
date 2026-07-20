FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY Meshtastic.Mqtt.csproj ./
RUN dotnet restore

# Copy the rest of the code
COPY . ./
RUN dotnet publish -c Release -o /app -f net10.0 --no-restore

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app ./

# Expose ports
EXPOSE 1883

ENTRYPOINT ["dotnet", "Meshtastic.Mqtt.dll", "/data/config.yaml", "/data/logs"]