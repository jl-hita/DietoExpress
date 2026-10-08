import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { PublicSeoService } from '../../servicios/public-seo.service';

@Component({
  selector: 'app-nutritionist-marketplace',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule],
  templateUrl: './nutritionist-marketplace.component.html',
  styles: [`
    .page{max-width:980px;margin:0 auto;padding:32px 20px}
    .hero{padding:44px 28px;border-radius:24px;background:linear-gradient(135deg,#eff6ff,#f8fafc)}
    h1{font-size:clamp(30px,6vw,52px);line-height:1.05;margin:0 0 16px}
    .lead{font-size:18px;line-height:1.6;max-width:720px}
    .grid{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin:28px 0}
    .card{padding:22px;border:1px solid #e2e8f0;border-radius:16px;background:#fff}
    .actions{display:flex;gap:12px;flex-wrap:wrap;margin-top:24px}
    @media(max-width:720px){.grid{grid-template-columns:1fr}.page{padding:20px 14px}}
  `]
})
export class NutritionistMarketplaceComponent implements OnInit {
  constructor(private readonly seo: PublicSeoService) {}
  ngOnInit(): void { this.seo.setMarketplaceLandingSeo(); }
}