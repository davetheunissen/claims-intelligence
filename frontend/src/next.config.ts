import type { NextConfig } from "next";

// In Docker compose the API is reachable via the service name.
// Set NEXT_PUBLIC_API_URL=http://api:8080 in the container environment to override.
const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

const nextConfig: NextConfig = {
  output: "standalone",
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/:path*` }];
  },
};

export default nextConfig;
