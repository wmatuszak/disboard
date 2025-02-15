FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
RUN apt update
RUN apt install ffmpeg libopus0 libopus-dev libsodium23 libsodium-dev -y

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=build /app .
ADD config /config
ADD sounds /sounds
ENTRYPOINT ["dotnet", "disboard.dll"]