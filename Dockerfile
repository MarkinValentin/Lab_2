# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY App/app.csproj App/
RUN dotnet restore App/app.csproj

COPY App/ App/
RUN dotnet publish App/app.csproj -c Release -o /out /p:UseAppHost=false

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /App

COPY --from=build /out/ /App/
COPY App/appsettings.json /App/appsettings.json

RUN adduser --disabled-password --gecos "" appuser && chown -R appuser:appuser /App
USER appuser

ENV PINGER_INTERVAL_SECONDS=300
ENV PINGER_LOG_FILE=""
ENV DB_USER=""
ENV DB_PASSWORD=""

ENTRYPOINT ["dotnet", "app.dll"]
