# 回避ビルドとパリィ特化の「ボスを倒すまでの時間」をざっくり比べるシミュレーター（python3 で実行。ゲーム本体では使わない）
import random
# とてもざっくりした戦闘シミュレーター（同じ腕前の人が、ビルドだけ変えたときのクリア時間を比べる）
P_PARRY=0.75          # パリィの成功率（受付 約0.42秒）
P_DODGE0=0.82         # 回避の成功率（受付が短くならないとき。向きを読まなくていいぶん少しやさしい）
def p_dodge(mult): return max(0.05,min(0.95,P_DODGE0*(0.30+0.70*mult)))  # 受付が短いほど下がる
def fight(b,hp=3000,need=(100,175),every=22,brk=3.8,seed=None):
    rnd=random.Random(seed);t=0;post=0;HP=hp;cnt=0;php=999
    atk=b['atk'];crit=b.get('crit',0.1);cm=b.get('cm',1.8);avg=atk*0.5*(1+crit*(cm-1))
    while HP>0:
        lv=min(need[1],need[0]+int(t/every)*25)
        # 敵の行動（約1.1秒に1回）
        r=rnd.random();t+=1.1
        kinds=['n']*60+['h']*12+['d']*10+['b']*8+['f']*10
        k=rnd.choice(kinds)
        hits=[k] if k!='f' else [('b' if rnd.random()<0.3 else 'n') for _ in range(3)]
        if k=='f':t+=0.6
        if rnd.random()<1.1/10:hits.append('z')   # 落下物（約10秒に1回）
        for h in hits:
            if h in('b','z'):   # はじけない→回避のみ
                ok=rnd.random()<p_dodge(b['dm']);post+= b['dp'] if ok else -25
                if ok and b.get('ct'):HP-=avg*b['ct_hits']*b['ct_mul']
                continue
            gain_p=25*(2 if h=='h' else 1)*b['pp']
            ev_p=P_PARRY*gain_p-(1-P_PARRY)*25
            pd=p_dodge(b['dm']);ev_d=pd*b['dp']-(1-pd)*25+(pd*avg*b['ct_hits']*b['ct_mul']/ (avg*6/25) if b.get('ct') else 0)
            if b['dp']>0 and ev_d>ev_p:
                ok=rnd.random()<pd;post+=b['dp'] if ok else -25
                if ok and b.get('ct'):HP-=avg*b['ct_hits']*b['ct_mul']
            else:
                ok=rnd.random()<(P_PARRY*(0.85 if h=='d' else 1));post+=gain_p if ok else -25
            post=max(0,post)
        if post>=lv:   # 体勢崩し → こすって攻撃（1秒に約6ヒット）
            HP-=avg*6*brk;t+=brk;post=0;cnt+=1
    return t
def avg(b,n=400,**kw):return sum(fight(b,seed=i,**kw) for i in range(n))/n
