import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HlmDialogImports, HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { describe, expect, it } from 'vitest';

@Component({
  standalone: true,
  imports: [...HlmSelectImports, ...HlmDialogImports],
  template: `
    <hlm-select [value]="selected()" (valueChange)="selected.set($event)">
      <hlm-select-trigger><hlm-select-value /></hlm-select-trigger>
      <hlm-select-content *hlmSelectPortal>
        <hlm-select-item value="one">One</hlm-select-item>
        <hlm-select-item value="two">Two</hlm-select-item>
      </hlm-select-content>
    </hlm-select>
    <hlm-dialog>
      <button hlmDialogTrigger>Open dialog</button>
      <hlm-dialog-content *hlmDialogPortal>
        <h2 hlmDialogTitle>Migration dialog</h2>
        <p hlmDialogDescription>Dialog content</p>
        <button hlmDialogClose>Done</button>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
class PrimitivesFixture {
  readonly selected = signal<unknown>('one');
}

@Component({
  standalone: true,
  template: '<h2>Dynamic content</h2>',
})
class DynamicContent {}

describe('Upgraded Spartan primitives', () => {
  it('opens the select and updates its value', async () => {
    const fixture = TestBed.createComponent(PrimitivesFixture);
    await fixture.whenStable();
    const trigger = fixture.nativeElement.querySelector('hlm-select-trigger button') as HTMLButtonElement;
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    trigger.click();
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const option = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'))
      .find((item) => item.textContent?.trim() === 'Two');
    expect(option).toBeDefined();
    option!.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()).toBe('two');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
  });

  it('opens and closes a template dialog', async () => {
    const fixture = TestBed.createComponent(PrimitivesFixture);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('[hlmDialogTrigger]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.querySelector('hlm-dialog-content')?.textContent).toContain('Migration dialog');
    (document.querySelector('button[hlmDialogClose]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.querySelector('hlm-dialog-content')).toBeNull();
  });

  it('renders dynamic dialog content with the typed context', async () => {
    const fixture = TestBed.createComponent(PrimitivesFixture);
    await fixture.whenStable();
    const service = TestBed.inject(HlmDialogService);
    const ref = service.open(DynamicContent);
    await fixture.whenStable();
    expect(document.querySelector('hlm-dialog-content')?.textContent).toContain('Dynamic content');
    ref.close();
    await fixture.whenStable();
    expect(document.querySelector('hlm-dialog-content')).toBeNull();
  });
});
