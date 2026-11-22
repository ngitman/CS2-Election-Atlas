const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** A month index (year x 12 + month) as "Jan 2026". */
export const monthLabel = (index: number | undefined | null): string => {
  if (index === undefined || index === null || index < 0) return "-";
  const year = Math.floor(index / 12);
  const month = ((index % 12) + 12) % 12;
  return `${MONTHS[month]} ${year}`;
};

export const pct = (v: number | undefined | null, digits = 1): string =>
  v === undefined || v === null || isNaN(v) ? "-" : `${(v * 100).toFixed(digits)}%`;

export const points = (margin: number): string => `${(margin * 100).toFixed(1)} pts`;

export const count = (n: number | undefined | null): string => {
  if (n === undefined || n === null) return "-";
  return Math.round(n)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, ",");
};

export const months = (n: number): string => (n === 1 ? "1 month" : `${n} months`);

/** Readable text color on a filled background (#rrggbb). */
export const inkFor = (hex: string): string => {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || "");
  if (!m) return "#ffffff";
  const [r, g, b] = [m[1], m[2], m[3]].map((h) => parseInt(h, 16) / 255);
  const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
  return luminance > 0.6 ? "#1b2230" : "#ffffff";
};

/** Approval on its 0-100 scale as "62%". */
export const approvalPct = (v: number | undefined | null): string =>
  v === undefined || v === null || isNaN(v) ? "-" : `${Math.round(v)}%`;

/** A #rrggbb color with an alpha, for tinted backgrounds. */
export const withAlpha = (hex: string, alpha: number): string => {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || "");
  if (!m) return `rgba(255, 255, 255, ${alpha})`;
  return `rgba(${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}, ${alpha})`;
};
