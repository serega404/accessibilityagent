FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine3.22 AS build
RUN apk add --no-cache clang lld build-base musl-dev
WORKDIR /src

COPY AccessibilityAgent/AccessibilityAgent.csproj AccessibilityAgent/
RUN dotnet restore AccessibilityAgent/AccessibilityAgent.csproj -r linux-musl-x64

COPY AccessibilityAgent/ AccessibilityAgent/
RUN dotnet publish AccessibilityAgent/AccessibilityAgent.csproj \
    -c Release \
    -r linux-musl-x64 \
    -o /out \
    --self-contained true \
    /p:PublishAot=true

# Runtime image with musl loader
FROM alpine:3.22
RUN apk add --no-cache ca-certificates
WORKDIR /app
COPY --from=build /out/AccessibilityAgent .
ENTRYPOINT ["./AccessibilityAgent"]