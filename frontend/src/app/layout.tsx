import type { Metadata } from "next";
import "./globals.css";
import { Providers } from "../components/Providers";

export const metadata: Metadata = {
  title: "Claims Intelligence · Microsoft Foundry Demo",
  description:
    "Auto Insurance Claims Intelligence: document classification, entity extraction, fraud detection, coverage recommendation, and customer letter generation powered by Microsoft Foundry.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
