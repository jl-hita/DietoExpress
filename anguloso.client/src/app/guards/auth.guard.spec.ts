import { Router } from '@angular/router';
import { AuthGuard } from './auth.guard';
import { AuthService } from '../servicios/auth.service';
import { AdminService } from '../servicios/admin.service';

describe('AuthGuard', () => {
  it('should be created', () => {
    const authService = {} as AuthService;
    const adminService = {} as AdminService;
    const router = {} as Router;
    const guard = new AuthGuard(authService, adminService, router);

    expect(guard).toBeTruthy();
  });
});
