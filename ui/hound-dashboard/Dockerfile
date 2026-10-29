FROM node:24.22.1 AS build
WORKDIR /app
COPY package*.json ./
RUN npm install --global npm@12.2.1
RUN npm ci
COPY . .
RUN npx ng build --configuration=production

FROM nginx:alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/hound-dashboard/browser /usr/share/nginx/html
EXPOSE 80
