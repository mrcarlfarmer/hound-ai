import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { TunerComponent } from './tuner.component';
import { TunerService } from '../../services/tuner.service';
import { TunerExperiment, PagedResult } from '../../models';

describe('TunerComponent', () => {
  const mockExperiment: TunerExperiment = {
    id: 'exp-1',
    houndName: 'AnalysisHound',
    timestamp: '2024-01-15T10:30:00Z',
    configBefore: '{"threshold":0.5}',
    configAfter: '{"threshold":0.6}',
    baselineScore: 0.72,
    candidateScore: 0.78,
    delta: 0.06,
    status: 'pending-review',
    rationale: 'Candidate improves signal accuracy.',
  };

  const emptyResult: PagedResult<TunerExperiment> = { items: [], totalCount: 0, page: 1, pageSize: 20 };

  function createComponent(experiments: TunerExperiment[] = [], totalCount = 0) {
    const mockTuner = {
      getExperiments: vi.fn().mockReturnValue(
        of({ items: experiments, totalCount, page: 1, pageSize: 20 } as PagedResult<TunerExperiment>),
      ),
      applyExperiment: vi.fn().mockReturnValue(of(undefined)),
      rejectExperiment: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [TunerComponent],
      providers: [{ provide: TunerService, useValue: mockTuner }],
    }).compileComponents();
    const fixture = TestBed.createComponent(TunerComponent);
    fixture.detectChanges();
    return { fixture, mockTuner };
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('should create', () => {
    const { fixture } = createComponent();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a table row for each experiment', () => {
    const { fixture } = createComponent([mockExperiment], 1);
    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('AnalysisHound');
    expect(rows[0].textContent).toContain('pending-review');
  });

  it('should show Apply and Reject buttons for pending-review experiments', () => {
    const { fixture } = createComponent([mockExperiment], 1);
    const applyBtn = fixture.nativeElement.querySelector('.btn-apply');
    const rejectBtn = fixture.nativeElement.querySelector('.btn-reject');
    expect(applyBtn).toBeTruthy();
    expect(rejectBtn).toBeTruthy();
  });

  it('should not show Apply/Reject buttons for applied experiments', () => {
    const { fixture } = createComponent([{ ...mockExperiment, status: 'applied' }], 1);
    const applyBtn = fixture.nativeElement.querySelector('.btn-apply');
    expect(applyBtn).toBeFalsy();
  });

  it('should update experiment status to applied when Apply is clicked', () => {
    const { fixture } = createComponent([{ ...mockExperiment }], 1);
    const applyBtn: HTMLButtonElement = fixture.nativeElement.querySelector('.btn-apply');
    applyBtn.click();
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.badge');
    expect(badge?.textContent?.trim()).toBe('applied');
  });

  it('should update experiment status to rejected when Reject is clicked', () => {
    const { fixture } = createComponent([{ ...mockExperiment }], 1);
    const rejectBtn: HTMLButtonElement = fixture.nativeElement.querySelector('.btn-reject');
    rejectBtn.click();
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.badge');
    expect(badge?.textContent?.trim()).toBe('rejected');
  });

  it('should show empty message when no experiments are returned', () => {
    const { fixture } = createComponent([], 0);
    const empty = fixture.nativeElement.querySelector('.empty');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No experiments found');
  });

  it('should show pagination when totalCount exceeds pageSize', () => {
    const manyExperiments = Array.from({ length: 20 }, (_, i) => ({
      ...mockExperiment,
      id: `exp-${i}`,
      status: 'improved' as const,
    }));
    const { fixture } = createComponent(manyExperiments, 50);
    const pagination = fixture.nativeElement.querySelector('.pagination');
    expect(pagination).toBeTruthy();
  });
});

