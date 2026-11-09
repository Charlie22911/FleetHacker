"""Compare with the primary-score section of the pinned upstream DCI routine."""
import ast,os,sys,unittest
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from fh_model import primary
class Primary(unittest.TestCase):
    def test_original_transform_inputs_and_mean(self):
        upstream=Path(os.environ['FH_ALETHEIA_SOURCE'])/'aletheialib/models.py'
        tree=ast.parse(upstream.read_text(encoding='utf-8'))
        fn=next(node for node in tree.body if isinstance(node,ast.FunctionDef) and node.name=='dci_si_method')
        start=next(i for i,n in enumerate(fn.body) if isinstance(n,ast.Assign) and ast.unparse(n.targets[0])=='A')
        end=next(i for i,n in enumerate(fn.body) if isinstance(n,ast.Assign) and ast.unparse(n.targets[0])=='aa_mean')
        block=compile(ast.Module(body=fn.body[start:end+1],type_ignores=[]),str(upstream),'exec')
        image=np.random.default_rng(22911).integers(0,256,(75,83,4),dtype=np.uint8)
        class Model:
            def __init__(self):self.inputs=[]
            def predict(self,batch,verbose=0):
                self.inputs.append(batch.copy());value=np.float32(np.mean(batch[:,:19,:23,:]));return np.array([[1-value,value]],dtype=np.float32)
        class NN:
            shape=(512,512,3)
            def __init__(self):self.model=Model()
        a,b=NN(),NN();scope=dict(np=np,A_files=['input'],B_files=['input'],A_nn=a,B_nn=b,imread=lambda _:image)
        exec(block,scope);reference=a.model.inputs[::2];ours=NN();actual=primary(ours,image)
        self.assertEqual(len(reference),32);self.assertEqual(len(ours.model.inputs),32)
        for x,y in zip(reference,ours.model.inputs):np.testing.assert_array_equal(x,y)
        self.assertEqual(actual,float(scope['aa_mean']))
if __name__=='__main__':unittest.main()
