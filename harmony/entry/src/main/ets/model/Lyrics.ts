// 歌词模型与 LRC 解析。鸿蒙无 TagLib，不支持内嵌标签解析，仅解析 .lrc 文本。
export interface LyricLine {
  timeMs: number;
  text: string;
}

export interface LyricsResult {
  lines: LyricLine[];
  source: string; // 'Local' | 'Cloud' | 'Lrclib' | 'Online' | ''
  raw: string;
}

export class LrcParser {
  // 解析 LRC：支持 [mm:ss.xx] / [mm:ss.xxx]（小数点或冒号分隔），一行可多个时间戳；
  // 纯元数据行（[ti:]/[ar:]/[al:] 等，无有效时间）自然被跳过；无时间戳的普通文本行忽略。
  static parse(text: string): LyricLine[] {
    const lines: LyricLine[] = [];
    if (!text) {
      return lines;
    }
    const lineRe = /^(\[[^\]]*\])+(.*)$/;
    const timeRe = /\[(\d+):(\d+)(?:[.:](\d+))?\]/g;
    const rawLines = text.split(/\r?\n/);
    for (const rl of rawLines) {
      const m = lineRe.exec(rl);
      if (!m) {
        continue;
      }
      const timePart = m[1];
      const content = (m[2] ?? '').trim();
      if (content === '') {
        continue;
      }
      timeRe.lastIndex = 0;
      let tm: RegExpExecArray | null;
      while ((tm = timeRe.exec(timePart)) !== null) {
        const min = parseInt(tm[1], 10);
        const sec = parseInt(tm[2], 10);
        const fracStr = tm[3] ?? '0';
        const frac = parseInt(fracStr, 10) * Math.pow(10, 3 - fracStr.length);
        const ms = (min * 60 + sec) * 1000 + (isNaN(frac) ? 0 : frac);
        lines.push({ timeMs: ms, text: content });
      }
    }
    lines.sort((a, b) => a.timeMs - b.timeMs);
    return lines;
  }
}
