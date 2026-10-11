/* cost_bench.c -- cost per body-step of buoyancy on the body's own mass (naive) vs bounded buoyancy
   (Bounded Buoyancy v1.1, Section 4.4; companion paper to ValerieX (VXXX) v2.2).
   gcc -O2 -o cost_bench cost_bench.c -lm && ./cost_bench */
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include <time.h>
#define G 9.80665
#define RHO 1000.0
#define C 0.5
#define CD 0.47
static double now(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return t.tv_sec+1e-9*t.tv_nsec;}
int main(void){
  int N=1000000, STEPS=200; const double dt=1.0/60.0;
  for(int pass=0;pass<2;pass++){ if(pass==1){N=10000;STEPS=20000;} printf("N = %d bodies, %d steps\n",N,STEPS);
  double *R=malloc(1000000*8),*m=malloc(1000000*8),*im=malloc(1000000*8),*z=malloc(1000000*8),*v=malloc(1000000*8),*map=malloc(1000000*8);
  for(int model=0;model<2;model++){
    srand(1);
    for(int i=0;i<N;i++){R[i]=0.01+0.2*rand()/(double)RAND_MAX; double r=0.01+0.9*rand()/(double)RAND_MAX;
      m[i]=r*RHO*4.0/3.0*M_PI*R[i]*R[i]*R[i]; im[i]=1.0/m[i]; z[i]=-R[i]+2*R[i]*rand()/(double)RAND_MAX; v[i]=0; map[i]=0;}
    double t0=now();
    for(int s=0;s<STEPS;s++){
      for(int i=0;i<N;i++){
        double Ri=R[i], h=Ri-z[i]; h=h<0?0:(h>2*Ri?2*Ri:h);
        double Vs=M_PI*h*h*(3*Ri-h)/3.0, V=4.0/3.0*M_PI*Ri*Ri*Ri;
        double Fb=-m[i]*G+RHO*Vs*G, kd=0.5*RHO*CD*M_PI*Ri*Ri*(Vs/V), vi=v[i], iM;
        if(model==0){ iM=im[i]; }
        else { double ma=C*RHO*Vs; iM=1.0/(m[i]+ma); double keep=(m[i]+map[i])*iM; vi*= (ma>map[i])?keep:1.0; map[i]=ma; }
        double vn=(vi+Fb*iM*dt)/(1.0+kd*fabs(vi)*dt*iM);
        v[i]=vn; z[i]+=vn*dt;
      }
    }
    double el=now()-t0; double chk=0; for(int i=0;i<N;i++) chk+=z[i];
    printf("%s: %.2f ns per body-step (checksum %.3f)\n", model?"bounded":"naive  ", 1e9*el/((double)N*STEPS), chk);
  }
  }
  return 0;
}
