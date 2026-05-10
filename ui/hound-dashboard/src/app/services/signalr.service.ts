import { Injectable, InjectionToken, inject } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { ActivityLog, WatchtowerEvent, OrderUpdate } from '../models';

export const SIGNALR_CONNECTION_FACTORY = new InjectionToken<() => signalR.HubConnection>(
  'SignalrConnectionFactory',
  {
    factory: () => () =>
      new signalR.HubConnectionBuilder()
        .withUrl('/hubs/activity')
        .withAutomaticReconnect()
        .build(),
  },
);

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private connectionFactory = inject(SIGNALR_CONNECTION_FACTORY);
  private hubConnection?: signalR.HubConnection;
  private connectionPromise?: Promise<void>;
  private activitySubject = new Subject<ActivityLog>();
  private watchtowerSubject = new Subject<WatchtowerEvent>();
  private orderUpdateSubject = new Subject<OrderUpdate>();

  onActivity$: Observable<ActivityLog> = this.activitySubject.asObservable();
  onWatchtowerEvent$: Observable<WatchtowerEvent> = this.watchtowerSubject.asObservable();
  onOrderUpdate$: Observable<OrderUpdate> = this.orderUpdateSubject.asObservable();

  connect(): void {
    if (this.connectionPromise) return;

    this.hubConnection = this.connectionFactory();

    this.hubConnection.on('OnActivity', (activity: ActivityLog) => {
      this.activitySubject.next(activity);
    });

    this.hubConnection.on('OnWatchtowerEvent', (event: WatchtowerEvent) => {
      this.watchtowerSubject.next(event);
    });

    this.hubConnection.on('OnOrderUpdate', (update: OrderUpdate) => {
      this.orderUpdateSubject.next(update);
    });

    this.connectionPromise = this.hubConnection.start().catch(err => console.error('SignalR connection error:', err));
  }

  disconnect(): void {
    this.hubConnection?.stop();
    this.connectionPromise = undefined;
  }

  subscribeToPack(packId: string): void {
    this.connectionPromise?.then(() => this.hubConnection?.invoke('SubscribeToPack', packId));
  }

  unsubscribeFromPack(packId: string): void {
    this.connectionPromise?.then(() => this.hubConnection?.invoke('UnsubscribeFromPack', packId));
  }
}
