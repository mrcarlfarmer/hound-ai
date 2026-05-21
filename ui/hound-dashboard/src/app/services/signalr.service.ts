import { Injectable, InjectionToken, inject } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { ActivityLog, OrderUpdate, GraphRun } from '../models';

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
  private orderUpdateSubject = new Subject<OrderUpdate>();
  private graphRunSubject = new Subject<GraphRun>();

  onActivity$: Observable<ActivityLog> = this.activitySubject.asObservable();
  onOrderUpdate$: Observable<OrderUpdate> = this.orderUpdateSubject.asObservable();
  onGraphRunUpdate$: Observable<GraphRun> = this.graphRunSubject.asObservable();

  connect(): void {
    if (this.connectionPromise) return;

    this.hubConnection = this.connectionFactory();

    this.hubConnection.on('OnActivity', (activity: ActivityLog) => {
      this.activitySubject.next(activity);
    });

    this.hubConnection.on('OnOrderUpdate', (update: OrderUpdate) => {
      this.orderUpdateSubject.next(update);
    });

    this.hubConnection.on('OnGraphRunUpdate', (run: GraphRun) => {
      this.graphRunSubject.next(run);
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
