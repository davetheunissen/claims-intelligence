import { createDarkTheme, createLightTheme, type BrandVariants } from "@fluentui/react-components";

// Primary navy ramp (#001272 → light tints) used for Fluent BrandVariants.
const brand: BrandVariants = {
  10: "#000418",
  20: "#000A30",
  30: "#001050",
  40: "#001272",
  50: "#0A1F8A",
  60: "#1A2EA0",
  70: "#2A3FB6",
  80: "#3A50C8",
  90: "#4A62D8",
  100: "#6479E0",
  110: "#8090E8",
  120: "#9CA8EE",
  130: "#B6BFF3",
  140: "#CDD3F7",
  150: "#E2E5FB",
  160: "#F1F2FD",
};

export const palette = {
  navy: "#001272",
  navyDark: "#000A30",
  navySoft: "#0A1F8A",
  cyan: "#00BCBE",
  cyanBright: "#00EBED",
  cyanLight: "#99F7F8",
  cyanSoft: "#CCFBFB",
  yellow: "#FFCF03",
  yellowSoft: "#FFEF99",
  success: "#31C85A",
  successDark: "#21883D",
  error: "#C5093B",
  warning: "#FF6D00",
  ink: "#212121",
  inkSoft: "#616161",
  inkMuted: "#9E9E9E",
};

export const darkTheme = {
  ...createDarkTheme(brand),
  colorBrandBackground2: "#0A1F8A",
  colorBrandForeground2: palette.cyanLight,
  colorBrandStroke2: palette.cyan,
};

export const lightTheme = {
  ...createLightTheme(brand),
  colorNeutralBackground1: "#FFFFFF",
  colorNeutralBackground1Hover: "#F5F7FB",
  colorNeutralBackground1Pressed: "#EAEEFB",
  colorNeutralBackground2: "#F5F7FB",
  colorNeutralBackground3: "#EAEEFB",
  colorNeutralBackground4: "#F5F7FB",
  colorNeutralBackground5: "#FFFFFF",
  colorNeutralBackground6: "#FFFFFF",
  colorNeutralStroke1: "#D6DAEC",
  colorNeutralStroke2: "#E2E5FB",
  colorNeutralStroke3: "#EAEEFB",
  colorNeutralForeground1: palette.navy,
  colorNeutralForeground2: "#1A2EA0",
  colorNeutralForeground3: "#3A50C8",
  colorBrandBackground: palette.navy,
  colorBrandBackgroundHover: palette.navySoft,
  colorBrandBackgroundPressed: palette.navyDark,
  colorBrandBackground2: "#EAEEFB",
  colorBrandForeground1: palette.navy,
  colorBrandForeground2: palette.navy,
  colorBrandStroke1: palette.navy,
  colorBrandStroke2: palette.cyan,
};

export const accent = {
  teal: palette.cyan,
  tealLight: palette.cyanLight,
  amber: palette.yellow,
  red: palette.error,
  green: palette.success,
};
