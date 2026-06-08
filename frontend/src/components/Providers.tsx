"use client";

import { useEffect, useState } from "react";
import { FluentProvider } from "@fluentui/react-components";
import { darkTheme, lightTheme } from "../lib/theme";
import { useThemeStore } from "../store/themeStore";

export function Providers({ children }: { children: React.ReactNode }) {
  const mode = useThemeStore((s) => s.mode);
  const theme = mode === "dark" ? darkTheme : lightTheme;
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
    document.body.dataset.theme = mode;
  }, [mode]);

  // Avoid SSR flash: render with light theme on server, then hydrate client.
  if (!mounted) {
    return (
      <FluentProvider theme={lightTheme} style={{ minHeight: "100vh" }}>
        {children}
      </FluentProvider>
    );
  }

  return (
    <FluentProvider theme={theme} style={{ minHeight: "100vh" }}>
      {children}
    </FluentProvider>
  );
}
