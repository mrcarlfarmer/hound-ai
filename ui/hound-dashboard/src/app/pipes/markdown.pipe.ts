import { Pipe, PipeTransform, SecurityContext } from '@angular/core';
import { Marked } from 'marked';
import { DomSanitizer } from '@angular/platform-browser';

@Pipe({
  name: 'markdown',
  standalone: true,
})
export class MarkdownPipe implements PipeTransform {
  private marked = new Marked({ gfm: true, async: false });

  constructor(private sanitizer: DomSanitizer) {}

  transform(value: string | null | undefined): string {
    if (!value) return '';
    const html = this.marked.parse(value) as string;
    return this.sanitizer.sanitize(SecurityContext.HTML, html) ?? '';
  }
}
