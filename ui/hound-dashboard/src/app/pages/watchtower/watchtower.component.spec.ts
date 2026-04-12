import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { WatchtowerComponent } from './watchtower.component';
import { WatchtowerEvent } from '../../models';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';

describe('WatchtowerComponent', () => {
  let watchtowerSubject: Subject<WatchtowerEvent>;
  let mockSignalr: Partial<SignalrService>;

  function createComponent(events: WatchtowerEvent[] = []) {
    TestBed.overrideProvider(ApiService, {
      useValue: { getWatchtowerEvents: vi.fn().mockReturnValue(of(events)) },
    });
    const fixture = TestBed.createComponent(WatchtowerComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(async () => {
    watchtowerSubject = new Subject<WatchtowerEvent>();
    mockSignalr = {
      connect: vi.fn(),
      disconnect: vi.fn(),
      onWatchtowerEvent$: watchtowerSubject,
    };

    await TestBed.configureTestingModule({
      imports: [WatchtowerComponent],
      providers: [
        { provide: ApiService, useValue: { getWatchtowerEvents: vi.fn().mockReturnValue(of([])) } },
        { provide: SignalrService, useValue: mockSignalr },
      ],
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = createComponent();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a table row for each event', () => {
    const events: WatchtowerEvent[] = [
      {
        id: '1',
        containerName: 'trading-pack',
        imageName: 'ghcr.io/hound-ai/trading-pack:latest',
        oldImageId: 'abc1234',
        newImageId: 'def5678',
        action: 'Updated',
        timestamp: '2026-04-12T10:00:00Z',
        rawPayload: '{}',
      },
      {
        id: '2',
        containerName: 'hound-api',
        imageName: 'ghcr.io/hound-ai/hound-api:latest',
        oldImageId: 'ghi9012',
        newImageId: 'jkl3456',
        action: 'Updated',
        timestamp: '2026-04-12T10:05:00Z',
        rawPayload: '{}',
      },
    ];

    const fixture = createComponent(events);

    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('trading-pack');
    expect(rows[1].textContent).toContain('hound-api');
  });

  it('should show empty message when no events are returned', () => {
    const fixture = createComponent([]);

    const empty = fixture.nativeElement.querySelector('.empty');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No watchtower events');
  });

  it('should display image IDs in mono font cells', () => {
    const events: WatchtowerEvent[] = [
      {
        id: '1',
        containerName: 'test',
        imageName: 'img:latest',
        oldImageId: 'old123',
        newImageId: 'new456',
        action: 'Updated',
        timestamp: '2026-04-12T10:00:00Z',
        rawPayload: '{}',
      },
    ];

    const fixture = createComponent(events);

    const monoCells = fixture.nativeElement.querySelectorAll('.mono');
    expect(monoCells.length).toBe(2);
    expect(monoCells[0].textContent).toContain('old123');
    expect(monoCells[1].textContent).toContain('new456');
  });
});
