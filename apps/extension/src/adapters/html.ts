const entities: Record<string, string> = {
  amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: " ", "#39": "'", "#x27": "'",
};

export function decodeEntities(text: string): string {
  return text.replace(/&(#x?[0-9a-f]+|[a-z]+);/gi, (whole, name: string) => {
    const known = entities[name.toLowerCase()];
    if (known !== undefined) {
      return known;
    }

    const numeric = /^#x/i.test(name) ? Number.parseInt(name.slice(2), 16) : /^#/.test(name) ? Number(name.slice(1)) : Number.NaN;
    return Number.isFinite(numeric) ? String.fromCodePoint(numeric) : whole;
  });
}

/** The words a piece of markup shows, one entry per run of text, in reading order. */
export function textsOf(html: string): string[] {
  return html
    .replace(/<(script|style|svg)\b[\s\S]*?<\/\1>/gi, " ")
    .split(/<[^>]*>/)
    .map((text) => decodeEntities(text).replace(/\s+/g, " ").trim())
    .filter((text) => text.length > 0);
}
