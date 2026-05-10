import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { Pack, HoundInfo, ActivityLog } from '../../models';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-pack-detail',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pack-detail.component.html',
  styles: []
})
export class PackDetailComponent implements OnInit, OnDestroy {
  pack?: Pack;
  hounds: HoundInfo[] = [];
  activities: ActivityLog[] = [];
  private sub?: Subscription;

  constructor(
    private route: ActivatedRoute,
    private api: ApiService,
    private signalr: SignalrService
  ) {}

  ngOnInit(): void {
    const packId = this.route.snapshot.paramMap.get('id')!;
    this.api.getPack(packId).subscribe(p => this.pack = p);
    this.api.getHounds(packId).subscribe(h => this.hounds = h);

    this.signalr.connect();
    this.signalr.subscribeToPack(packId);
    this.sub = this.signalr.onActivity$.subscribe(a => {
      this.activities.unshift(a);
    });
  }

  ngOnDestroy(): void {
    if (this.pack) this.signalr.unsubscribeFromPack(this.pack.id);
    this.sub?.unsubscribe();
    this.signalr.disconnect();
  }
}
