import { Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { ActivityLog, WatchtowerEvent } from '../models';

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private hubConnection?: signalR.HubConnection;
  private activitySubject = new Subject<ActivityLog>();
  private watchtowerSubject = new Subject<WatchtowerEvent>();

  onActivity$: Observable<ActivityLog> = this.activitySubject.asObservable();
  onWatchtowerEvent$: Observable<WatchtowerEvent> = this.watchtowerSubject.asObservable();

  connect(): void {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('http://localhost:5000/hubs/activity')
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('OnActivity', (activity: ActivityLog) => {
      this.activitySubject.next(activity);
    });

    this.hubConnection.on('OnWatchtowerEvent', (event: WatchtowerEvent) => {
      this.watchtowerSubject.next(event);
    });

    this.hubConnection.start().catch(err => console.error('SignalR connection error:', err));
  }

  disconnect(): void {
    this.hubConnection?.stop();
  }

  subscribeToPack(packId: string): void {
    this.hubConnection?.invoke('SubscribeToPack', packId);
  }

  unsubscribeFromPack(packId: string): void {
    this.hubConnection?.invoke('UnsubscribeFromPack', packId);
  }
}
