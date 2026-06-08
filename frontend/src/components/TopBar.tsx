"use client";

import {
  Avatar,
  Body1,
  Button,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Subtitle2,
  Tooltip,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { WeatherMoon20Regular, WeatherSunny20Regular } from "@fluentui/react-icons";
import { useRouter } from "next/navigation";
import { useThemeStore } from "../store/themeStore";
import { useClaimStore } from "../store/claimStore";

const useStyles = makeStyles({
  root: {
    height: "56px",
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingLeft: "24px",
    paddingRight: "24px",
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  brand: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    cursor: "pointer",
    background: "none",
    border: "none",
    padding: 0,
    fontFamily: "inherit",
  },
  dot: {
    width: "10px",
    height: "10px",
    borderRadius: "50%",
    background: "linear-gradient(135deg, #00BCBE 0%, #001272 100%)",
    flexShrink: 0,
  },
  right: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
  },
});

interface TopBarProps {
  /** Whether MSAL is configured and user-facing auth should be shown. */
  msalConfigured?: boolean;
  /** Current user account from MSAL (client-side only). */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  account?: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  instance?: any;
}

export function TopBar({ msalConfigured = false, account, instance }: TopBarProps) {
  const styles = useStyles();
  const router = useRouter();
  const themeMode = useThemeStore((s) => s.mode);
  const toggleTheme = useThemeStore((s) => s.toggle);
  const reset = useClaimStore((s) => s.reset);

  const goHome = () => {
    reset();
    router.push("/");
  };

  const initials = account?.name
    ? account.name
        .split(" ")
        .map((p: string) => p[0])
        .slice(0, 2)
        .join("")
        .toUpperCase()
    : "??";

  return (
    <header className={styles.root}>
      <button
        className={styles.brand}
        onClick={goHome}
        type="button"
        aria-label="Go to home — Claims Intelligence"
      >
        <span className={styles.dot} />
        <Subtitle2
          style={{ color: tokens.colorBrandForeground1, fontWeight: 700, letterSpacing: "0.02em" }}
        >
          Claims Intelligence
        </Subtitle2>
        <Body1 style={{ opacity: 0.55 }}>· Powered by Microsoft Foundry</Body1>
      </button>
      <div className={styles.right}>
        <Tooltip
          content={themeMode === "dark" ? "Switch to light mode" : "Switch to dark mode"}
          relationship="label"
        >
          <Button
            appearance="subtle"
            icon={themeMode === "dark" ? <WeatherSunny20Regular /> : <WeatherMoon20Regular />}
            onClick={toggleTheme}
            aria-label={themeMode === "dark" ? "Switch to light mode" : "Switch to dark mode"}
          />
        </Tooltip>
        {msalConfigured && account && instance ? (
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button
                appearance="subtle"
                icon={<Avatar name={account.name ?? "?"} initials={initials} size={28} />}
              />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem disabled>{account.username}</MenuItem>
                <MenuItem onClick={() => instance.logoutRedirect()}>Sign out</MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        ) : msalConfigured ? (
          <Button appearance="primary" onClick={() => instance?.loginRedirect()}>
            Sign in
          </Button>
        ) : null}
      </div>
    </header>
  );
}
